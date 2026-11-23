using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Facturacion;
using PosVentas.Domain.Seguridad;
using PosVentas.WebAPI.Security;

namespace PosVentas.WebAPI.Controllers;

/// <summary>Configuración fiscal del comercio autenticado (facturación electrónica ARCA). Solo administradores.</summary>
[ApiController]
[Route("api/comercio")]
public sealed class ComercioController(ISender mediator) : ControllerBase
{
    /// <summary>Datos fiscales y estado del certificado. Nunca devuelve el certificado ni la clave privada.</summary>
    [HttpGet("configuracion-fiscal")]
    [AutorizarPermiso(Permisos.FacturacionConfigurar)]
    [ProducesResponseType<ConfiguracionFiscalDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConfiguracionFiscalDto>> ObtenerConfiguracion(CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerConfiguracionFiscalQuery(), ct));

    [HttpPut("configuracion-fiscal")]
    [AutorizarPermiso(Permisos.FacturacionConfigurar)]
    [ProducesResponseType<ConfiguracionFiscalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConfiguracionFiscalDto>> ActualizarConfiguracion(ActualizarDatosFiscalesCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command, ct));

    /// <summary>Carga el certificado digital de ARCA (.p12, o .crt + .key en PEM). Se guarda cifrado.</summary>
    [HttpPut("certificado-afip")]
    [AutorizarPermiso(Permisos.FacturacionConfigurar)]
    [RequestSizeLimit(256 * 1024)]
    [ProducesResponseType<ConfiguracionFiscalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConfiguracionFiscalDto>> CargarCertificado(CargarCertificadoAfipCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command, ct));

    /// <summary>Prueba la conexión con ARCA, la autenticación del certificado y el último número autorizado.</summary>
    [HttpPost("afip/verificacion")]
    [AutorizarPermiso(Permisos.FacturacionConfigurar)]
    [ProducesResponseType<EstadoServicioAfip>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<EstadoServicioAfip>> VerificarConexion(CancellationToken ct)
        => Ok(await mediator.Send(new VerificarConexionAfipCommand(), ct));
}
