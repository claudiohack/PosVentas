using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosVentas.Application.Common.Security;
using PosVentas.Application.Features.Clientes;
using PosVentas.Domain.Enums;

namespace PosVentas.WebAPI.Controllers;

[ApiController]
[Route("api/clientes")]
[Authorize(Policy = Politicas.Operador)]
public sealed class ClientesController(ISender mediator) : ControllerBase
{
    /// <summary>Clientes del comercio con su saldo y crédito disponible.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClienteDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClienteDto>>> Listar(
        [FromQuery] string? buscar, [FromQuery] bool soloCuentaCorriente, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerClientesQuery(buscar, soloCuentaCorriente), ct));

    [HttpPost]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<ClienteDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClienteDto>> Crear(CrearClienteCommand command, CancellationToken ct)
    {
        var cliente = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(CuentaCorriente), new { id = cliente.Id }, cliente);
    }

    /// <summary>Habilita / deshabilita la cuenta corriente y fija el límite de crédito.</summary>
    [HttpPut("{id:guid}/credito")]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<ClienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ClienteDto>> ActualizarCredito(Guid id, CreditoRequest request, CancellationToken ct)
        => Ok(await mediator.Send(new ActualizarCreditoClienteCommand(id, request.PermiteCuentaCorriente, request.LimiteCredito), ct));

    /// <summary>Saldo actual e historial de movimientos (cargos y abonos), más recientes primero.</summary>
    [HttpGet("{id:guid}/cuenta-corriente")]
    [ProducesResponseType<CuentaCorrienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CuentaCorrienteDto>> CuentaCorriente(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new ObtenerCuentaCorrienteQuery(id), ct));

    /// <summary>Recibo de cobranza: reduce el saldo del cliente e ingresa el dinero en el turno de caja indicado.</summary>
    [HttpPost("{id:guid}/cobranzas")]
    [ProducesResponseType<ReciboCobranzaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReciboCobranzaDto>> RegistrarCobranza(Guid id, CobranzaRequest request, CancellationToken ct)
    {
        var recibo = await mediator.Send(
            new RegistrarCobranzaCommand(id, request.Monto, request.Medio, request.CajaSesionId, request.Observaciones), ct);
        return StatusCode(StatusCodes.Status201Created, recibo);
    }

    public sealed record CreditoRequest(bool PermiteCuentaCorriente, decimal LimiteCredito);
    public sealed record CobranzaRequest(decimal Monto, MedioPago Medio, Guid CajaSesionId, string? Observaciones);
}
