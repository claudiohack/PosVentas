namespace PosVentas.Client.Models;

public sealed record PeriodoDto(DateOnly Desde, DateOnly Hasta);

public sealed record IngresoPorMedioDto(MedioPago Medio, decimal Total, int Operaciones, decimal Porcentaje);

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
    int VentasPendientesDeFacturar);

public sealed record TopProductoDto(
    int Posicion, Guid ProductoId, string Producto, string? Sku, string? Categoria,
    decimal Unidades, decimal Recaudacion, decimal VentaNeta, decimal Costo, decimal Margen, int Operaciones);

public sealed record TopProductosDto(PeriodoDto Periodo, string OrdenadoPor, IReadOnlyList<TopProductoDto> Productos);

public sealed record RentabilidadCategoriaDto(
    Guid CategoriaId, string Categoria, decimal Unidades, decimal Recaudacion, decimal VentaNeta,
    decimal Costo, decimal Margen, decimal MargenPorcentaje, decimal ParticipacionPorcentaje);

public sealed record RentabilidadPorCategoriaDto(PeriodoDto Periodo, IReadOnlyList<RentabilidadCategoriaDto> Categorias);

public sealed record CierreCajaDto(
    Guid SesionId, Guid CajaId, string Caja, DateTime FechaAperturaUtc, DateTime FechaCierreUtc,
    string UsuarioApertura, string? UsuarioCierre, decimal MontoApertura, decimal EfectivoEsperado,
    decimal EfectivoContado, decimal Diferencia, string Resultado, int CantidadVentas, decimal TotalVendido,
    decimal TotalPagosProveedores, string? Observaciones);

public sealed record ReporteCierresCajaDto(
    PeriodoDto Periodo, int CantidadTurnos, int TurnosExactos, int TurnosConSobrante, int TurnosConFaltante,
    decimal TotalSobrantes, decimal TotalFaltantes, decimal DiferenciaNeta, decimal TotalVendido,
    IReadOnlyList<CierreCajaDto> Turnos);

/// <summary>Rangos rápidos del selector de período del dashboard.</summary>
public enum RangoRapido
{
    Hoy,
    EstaSemana,
    EsteMes,
    Personalizado
}
