using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Clientes;

public sealed record ClienteDto(
    Guid Id,
    string RazonSocial,
    string? Documento,
    CondicionIva CondicionIva,
    string? Telefono,
    string? Email,
    string? Direccion,
    bool PermiteCuentaCorriente,
    decimal LimiteCredito,
    decimal SaldoActual,
    decimal CreditoDisponible,
    bool EsConsumidorFinal,
    bool EstaActivo);

public sealed record MovimientoCuentaCorrienteDto(
    Guid Id,
    DateTime FechaUtc,
    TipoMovimientoCuentaCorriente Tipo,
    decimal Monto,
    decimal SaldoResultante,
    Guid? VentaId,
    Guid? ReciboCobranzaId,
    string Descripcion);

public sealed record CuentaCorrienteDto(ClienteDto Cliente, IReadOnlyList<MovimientoCuentaCorrienteDto> Movimientos);

public sealed record ReciboCobranzaDto(
    Guid Id,
    Guid ClienteId,
    string Cliente,
    Guid CajaSesionId,
    MedioPago Medio,
    decimal Monto,
    decimal SaldoAnterior,
    decimal SaldoResultante,
    DateTime FechaUtc);

internal static class ClienteMapper
{
    public static ClienteDto ToDto(Cliente c) => new(
        c.Id, c.RazonSocial, c.Documento, c.CondicionIva, c.Telefono, c.Email, c.Direccion,
        c.PermiteCuentaCorriente, c.LimiteCredito, c.SaldoActual, c.CreditoDisponible,
        c.EsConsumidorFinalGenerico, c.EstaActivo);
}
