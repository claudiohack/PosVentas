using PosVentas.Domain.Enums;

namespace PosVentas.Application.Features.Reportes;

/// <summary>Período informado (fechas calendario de la Argentina, ambos extremos incluidos).</summary>
public sealed record PeriodoDto(DateOnly Desde, DateOnly Hasta);

public sealed record IngresoPorMedioDto(MedioPago Medio, decimal Total, int Operaciones, decimal Porcentaje);

/// <summary>
/// Resumen financiero del período. Importes en pesos.
/// - TotalVendido incluye IVA; VentaNeta no.
/// - MargenBruto = VentaNeta − CostoEstimado (costo de cada renglón congelado al momento de vender).
/// </summary>
public sealed record DashboardResumenDto(
    PeriodoDto Periodo,
    int CantidadVentas,
    decimal TotalVendido,
    decimal VentaNeta,
    decimal TotalIva,
    decimal CostoEstimado,
    decimal MargenBruto,
    decimal MargenPorcentaje,
    decimal TicketPromedio,
    IReadOnlyList<IngresoPorMedioDto> IngresosPorMedio,
    int ProductosConStockBajo,
    int VentasPendientesDeFacturar,
    int CantidadVentasAnuladas,
    decimal TotalAnulado);

public enum OrdenRanking
{
    Unidades = 1,
    Recaudacion = 2
}

public sealed record TopProductoDto(
    int Posicion,
    Guid ProductoId,
    string Producto,
    string? Sku,
    string? Categoria,
    decimal Unidades,
    decimal Recaudacion,
    decimal VentaNeta,
    decimal Costo,
    decimal Margen,
    int Operaciones);

public sealed record TopProductosDto(PeriodoDto Periodo, OrdenRanking OrdenadoPor, IReadOnlyList<TopProductoDto> Productos);

public sealed record RentabilidadCategoriaDto(
    Guid CategoriaId,
    string Categoria,
    decimal Unidades,
    decimal Recaudacion,
    decimal VentaNeta,
    decimal Costo,
    decimal Margen,
    decimal MargenPorcentaje,
    decimal ParticipacionPorcentaje);

public sealed record RentabilidadPorCategoriaDto(PeriodoDto Periodo, IReadOnlyList<RentabilidadCategoriaDto> Categorias);

public sealed record CierreCajaDto(
    Guid SesionId,
    Guid CajaId,
    string Caja,
    DateTime FechaAperturaUtc,
    DateTime FechaCierreUtc,
    string UsuarioApertura,
    string? UsuarioCierre,
    decimal MontoApertura,
    decimal EfectivoEsperado,
    decimal EfectivoContado,
    decimal Diferencia,
    string Resultado,
    int CantidadVentas,
    decimal TotalVendido,
    decimal TotalPagosProveedores,
    string? Observaciones);

/// <summary>Auditoría de cajas: turnos cerrados en el período y diferencias de arqueo acumuladas.</summary>
public sealed record ReporteCierresCajaDto(
    PeriodoDto Periodo,
    int CantidadTurnos,
    int TurnosExactos,
    int TurnosConSobrante,
    int TurnosConFaltante,
    decimal TotalSobrantes,
    decimal TotalFaltantes,
    decimal DiferenciaNeta,
    decimal TotalVendido,
    IReadOnlyList<CierreCajaDto> Turnos);
