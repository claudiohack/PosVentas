using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Entities;

namespace PosVentas.Infrastructure.Security;

/// <summary>
/// Emite JWT firmados (HS256) con los claims uid, tid (comercio) y role.
/// </summary>
public sealed class JwtTokenGenerator(IOptions<JwtOptions> options) : IJwtTokenGenerator
{
    private readonly JsonWebTokenHandler _handler = new();

    public TokenGenerado Generar(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        if (usuario.TenantId == Guid.Empty)
            throw new InvalidOperationException("No se puede emitir un token para un usuario sin comercio.");

        var config = options.Value;
        var ahora = DateTime.UtcNow;
        var expira = ahora.AddMinutes(config.ExpiracionMinutos);
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = config.Issuer,
            Audience = config.Audience,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira,
            SigningCredentials = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [PosVentasClaims.UsuarioId] = usuario.Id.ToString(),
                [PosVentasClaims.TenantId] = usuario.TenantId.ToString(),
                [PosVentasClaims.Rol] = usuario.Rol.ToString(),
                [PosVentasClaims.Nombre] = usuario.NombreCompleto
            }
        };

        return new TokenGenerado(_handler.CreateToken(descriptor), expira);
    }
}
