using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PosVentas.Application;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Enums;
using Microsoft.AspNetCore.HttpOverrides;
using PosVentas.Infrastructure;
using PosVentas.Infrastructure.Persistence;
using PosVentas.Infrastructure.Security;
using PosVentas.WebAPI;
using PosVentas.WebAPI.Middlewares;
using PosVentas.WebAPI.Services;

const string CorsClientPolicy = "PosVentasClient";

var builder = WebApplication.CreateBuilder(args);

// ---------- Capas ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<PosVentas.Application.Features.Facturacion.FacturacionOptions>(
    builder.Configuration.GetSection(PosVentas.Application.Features.Facturacion.FacturacionOptions.SectionName));

// ---------- Contexto del request (tenant / usuario desde el JWT) ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// ---------- Errores en formato ProblemDetails ----------
builder.Services.AddProblemDetails();
builder.Services.AddTransient<GlobalExceptionMiddleware>();
builder.Services.AddTransient<ValidacionSesionMiddleware>();

// ---------- Autenticación JWT ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = PosVentasClaims.Nombre,
            RoleClaimType = PosVentasClaims.Rol
        };
    });

// ---------- Autorización: seguro por defecto ----------
// Todo endpoint exige usuario autenticado con comercio, salvo los marcados [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(PosVentasClaims.TenantId)
        .Build())
    .AddPolicy(Politicas.SoloAdministrador, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PosVentasClaims.TenantId)
        .RequireRole(nameof(RolUsuario.Administrador)))
    .AddPolicy(Politicas.Supervision, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PosVentasClaims.TenantId)
        .RequireRole(nameof(RolUsuario.Administrador), nameof(RolUsuario.Supervisor)))
    .AddPolicy(Politicas.AdministradorGlobal, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PosVentasClaims.TenantId)
        .RequireRole(nameof(RolUsuario.Administrador))
        .RequireClaim(PosVentasClaims.AdministradorPlataforma, "1"))
    .AddPolicy(Politicas.Operador, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PosVentasClaims.TenantId)
        .RequireRole(nameof(RolUsuario.Administrador), nameof(RolUsuario.Supervisor), nameof(RolUsuario.Cajero)));

// ---------- Rate limiting en endpoints de autenticación ----------
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPorMinuto", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---------- CORS para el cliente Blazor ----------
var origenesPermitidos = builder.Configuration.GetSection("Cors:OrigenesPermitidos").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(CorsClientPolicy, policy => policy
    .WithOrigins(origenesPermitidos)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// ---------- Detrás de un reverse proxy (Nginx en Docker / DigitalOcean) ----------
// Con Proxy:ConfiarEnEncabezados=true se toman X-Forwarded-For / -Proto del proxy: la IP real del cliente
// (rate limiting, logs) y el esquema https original. Solo activarlo cuando la API NO está expuesta directo.
var detrasDeProxy = builder.Configuration.GetValue("Proxy:ConfiarEnEncabezados", false);
if (detrasDeProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownIPNetworks.Clear(); // la red interna de Docker: el proxy es el único que llega a la API
        o.KnownProxies.Clear();
    });
}

var app = builder.Build();

// Contenedores: migraciones y comercio operador de la plataforma al arrancar (idempotente).
if (app.Configuration.GetValue("BaseDatos:InicializarAlArrancar", false))
    await InicializadorBaseDatos.InicializarAsync(app.Services, aplicarMigraciones: true);

if (detrasDeProxy)
    app.UseForwardedHeaders();

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseHsts();
}

// En Docker el TLS lo termina el proxy: la API escucha http interno y no redirige.
if (app.Configuration.GetValue("Https:Redireccionar", true))
    app.UseHttpsRedirection();
app.UseCors(CorsClientPolicy);
app.UseAuthentication();
app.UseMiddleware<ValidacionSesionMiddleware>(); // revoca tokens de usuarios desactivados o con clave/rol cambiados
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { estado = "ok" })).AllowAnonymous();

app.Run();

/// <summary>Expuesto para pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
