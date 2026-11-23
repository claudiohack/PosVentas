using FluentValidation;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Suscripciones;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Suscripciones;

namespace PosVentas.Application.Features.Auth;

/// <summary>
/// Respuesta de login / alta de comercio. Los permisos son para que la UI muestre u oculte opciones:
/// la API los vuelve a verificar en cada request contra la base.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiraUtc,
    Guid UsuarioId,
    string NombreCompleto,
    Guid RolId,
    string Rol,
    bool EsAdministrador,
    IReadOnlyList<string> Permisos,
    Guid ComercioId,
    string Comercio,
    bool EsAdministradorPlataforma,
    SuscripcionResumenDto Suscripcion)
{
    /// <summary>Los permisos son los del rol menos los de módulos que el plan no incluye.</summary>
    internal static AuthResponse Crear(TokenGenerado token, Domain.Entities.Usuario usuario, Domain.Entities.Rol rol,
        Domain.Entities.Comercio comercio, bool esAdministradorPlataforma, SuscripcionVigente suscripcion) =>
        new(token.AccessToken, token.ExpiraUtc, usuario.Id, usuario.NombreCompleto, rol.Id, rol.Nombre, rol.EsAdministrador,
            Roles.CatalogoPermisos.Ordenar(suscripcion.FiltrarPermisos(rol.PermisosEfectivos())), comercio.Id, comercio.Nombre,
            esAdministradorPlataforma, SuscripcionResumenDto.Desde(suscripcion));
}

/// <summary>Sesión vigente según la base (GET /api/auth/sesion): la UI refresca sus permisos sin volver a ingresar.</summary>
public sealed record SesionDto(
    Guid UsuarioId,
    string NombreCompleto,
    Guid RolId,
    string Rol,
    bool EsAdministrador,
    IReadOnlyList<string> Permisos,
    Guid ComercioId,
    string Comercio,
    bool EsAdministradorPlataforma,
    SuscripcionResumenDto Suscripcion);

/// <summary>Lo que la UI necesita de la suscripción para avisar (prueba, gracia, solo lectura).</summary>
public sealed record SuscripcionResumenDto(
    EstadoSuscripcion Estado,
    bool EnPrueba,
    DateTime? VenceUtc,
    DateTime? FinGraciaUtc,
    int DiasRestantes,
    string? Plan,
    IReadOnlyList<string> Modulos,
    bool SinRestricciones)
{
    internal static SuscripcionResumenDto Desde(SuscripcionVigente s) =>
        new(s.Estado, s.EnPrueba, s.VenceUtc, s.FinGraciaUtc, s.DiasRestantes, s.Plan,
            ModulosPlan.Todos.Where(s.Modulos.Contains).ToList(), s.SinRestricciones);
}

internal static class AuthValidationRules
{
    public const string PatronSlug = "^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$";
    public const string PatronUsername = "^[a-zA-Z0-9._-]{3,50}$";
    public const int PasswordLongitudMinima = 10;
    public const int PasswordLongitudMaxima = 128;

    /// <summary>Complejidad mínima de contraseña: 10 a 128 caracteres, con letras y números.</summary>
    public static IRuleBuilderOptions<T, string> PasswordSegura<T>(this IRuleBuilder<T, string> regla) =>
        regla.NotEmpty()
            .MinimumLength(PasswordLongitudMinima)
            .MaximumLength(PasswordLongitudMaxima)
            .Must(p => p is not null && p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("La contraseña debe tener al menos 10 caracteres e incluir letras y números.");
}
