using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Cajas;

public sealed record SesionCajaDto(
    Guid Id,
    Guid CajaId,
    string Caja,
    Guid UsuarioAperturaId,
    DateTime FechaAperturaUtc,
    decimal MontoApertura,
    bool EnCurso);

public sealed record CajaDto(Guid Id, string Nombre, bool EstaAbierta, SesionCajaDto? SesionActiva);

public sealed record TotalPorMedioDto(MedioPago Medio, decimal Total);

/// <summary>
/// Resumen del turno. Mientras está abierto sirve de vista previa del arqueo;
/// cerrado incluye lo contado y la diferencia.
/// </summary>
public sealed record ResumenSesionDto(
    Guid SesionId,
    Guid CajaId,
    string Caja,
    bool EnCurso,
    DateTime FechaAperturaUtc,
    DateTime? FechaCierreUtc,
    decimal MontoApertura,
    int CantidadVentas,
    decimal TotalVendido,
    decimal TotalVentasCuentaCorriente,
    int CantidadCobranzas,
    decimal TotalCobranzas,
    IReadOnlyList<TotalPorMedioDto> TotalesPorMedio,
    decimal EfectivoEsperado,
    decimal? MontoCierreReal,
    decimal? DiferenciaArqueo,
    string? ResultadoArqueo,
    string? Observaciones);
