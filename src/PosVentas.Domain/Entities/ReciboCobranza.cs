using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Recibo de una entrega a cuenta. Tiene doble impacto:
/// reduce el saldo del cliente y registra el ingreso del dinero en el turno de caja activo.
/// </summary>
public sealed class ReciboCobranza : TenantEntity
{
    public const int LongitudMaximaObservaciones = 250;

    /// <summary>Medios aceptados para cobrar cuentas corrientes.</summary>
    public static readonly IReadOnlySet<MedioPago> MediosPermitidos =
        new HashSet<MedioPago> { MedioPago.Efectivo, MedioPago.Transferencia, MedioPago.QR };

    public Guid ClienteId { get; private set; }
    public Guid CajaSesionId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public MedioPago Medio { get; private set; }
    public decimal Monto { get; private set; }
    public decimal SaldoAnterior { get; private set; }
    public decimal SaldoResultante { get; private set; }
    public DateTime FechaUtc { get; private set; }
    public string? Observaciones { get; private set; }

    private ReciboCobranza(Guid id) : base(id) { }

    // Requerido por EF Core
    private ReciboCobranza() { }

    /// <summary>
    /// Registra la cobranza y devuelve los tres registros a persistir juntos:
    /// el recibo, el abono en la cuenta corriente y el ingreso en la caja.
    /// </summary>
    public static (ReciboCobranza Recibo, MovimientoCuentaCorriente Abono, MovimientoCaja IngresoCaja) Registrar(
        Cliente cliente, CajaSesion sesion, Guid usuarioId, MedioPago medio, decimal monto, string? observaciones)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(sesion);
        if (usuarioId == Guid.Empty)
            throw new DomainException("El usuario que cobra es obligatorio.");
        if (!MediosPermitidos.Contains(medio))
            throw new DomainException("Las cobranzas solo se aceptan en efectivo, transferencia o QR.");
        if (!sesion.EnCurso)
            throw new DomainException("No se puede cobrar en una caja cerrada.");

        var saldoAnterior = cliente.SaldoActual;
        var recibo = new ReciboCobranza(Guid.NewGuid())
        {
            ClienteId = cliente.Id,
            CajaSesionId = sesion.Id,
            UsuarioId = usuarioId,
            Medio = medio,
            Monto = monto,
            SaldoAnterior = saldoAnterior,
            FechaUtc = DateTime.UtcNow,
            Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim()
        };

        var abono = cliente.RegistrarAbono(monto, recibo.Id, usuarioId); // valida monto > 0 y <= deuda
        recibo.SaldoResultante = cliente.SaldoActual;

        sesion.RegistrarCobranza(recibo);
        var ingreso = MovimientoCaja.PorCobranza(sesion, recibo);

        return (recibo, abono, ingreso);
    }
}
