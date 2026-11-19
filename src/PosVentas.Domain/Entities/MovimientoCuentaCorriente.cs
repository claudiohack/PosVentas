using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Asiento de la cuenta corriente de un cliente. Cargo = venta a crédito; Abono = recibo de cobranza.
/// Guarda el saldo resultante para reconstruir el historial sin recalcular.
/// </summary>
public sealed class MovimientoCuentaCorriente : TenantEntity
{
    public const int LongitudMaximaDescripcion = 200;

    public Guid ClienteId { get; private set; }
    public TipoMovimientoCuentaCorriente Tipo { get; private set; }

    /// <summary>Importe siempre positivo; el signo lo da el tipo.</summary>
    public decimal Monto { get; private set; }

    public decimal SaldoResultante { get; private set; }
    public Guid? VentaId { get; private set; }
    public Guid? ReciboCobranzaId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public DateTime FechaUtc { get; private set; }
    public string Descripcion { get; private set; } = string.Empty;

    private MovimientoCuentaCorriente(Guid id) : base(id) { }

    // Requerido por EF Core
    private MovimientoCuentaCorriente() { }

    internal static MovimientoCuentaCorriente Crear(
        Guid clienteId, TipoMovimientoCuentaCorriente tipo, decimal monto, decimal saldoResultante,
        Guid? ventaId, Guid? reciboId, Guid usuarioId, string descripcion) => new(Guid.NewGuid())
    {
        ClienteId = clienteId,
        Tipo = tipo,
        Monto = monto,
        SaldoResultante = saldoResultante,
        VentaId = ventaId,
        ReciboCobranzaId = reciboId,
        UsuarioId = usuarioId,
        FechaUtc = DateTime.UtcNow,
        Descripcion = descripcion
    };
}
