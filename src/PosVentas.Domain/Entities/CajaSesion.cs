using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Turno de una caja: apertura con fondo inicial, y cierre con arqueo (sobrante / faltante).
/// </summary>
public sealed class CajaSesion : TenantEntity
{
    public Guid CajaId { get; private set; }
    public Caja? Caja { get; private set; }

    public Guid UsuarioAperturaId { get; private set; }
    public Guid? UsuarioCierreId { get; private set; }

    public DateTime FechaAperturaUtc { get; private set; }
    public DateTime? FechaCierreUtc { get; private set; }

    public decimal MontoApertura { get; private set; }
    public decimal? MontoCierreSistema { get; private set; }
    public decimal? MontoCierreReal { get; private set; }

    /// <summary>Positivo = sobrante, negativo = faltante, cero = caja perfecta.</summary>
    public decimal? DiferenciaArqueo { get; private set; }

    public bool EnCurso { get; private set; }
    public string? Observaciones { get; private set; }

    public int CantidadVentas { get; private set; }
    public decimal TotalVendido { get; private set; }
    public int CantidadCobranzas { get; private set; }
    public decimal TotalCobranzas { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private CajaSesion(Guid id) : base(id) { }

    // Requerido por EF Core
    private CajaSesion() { }

    public static CajaSesion Abrir(Caja caja, Guid usuarioAperturaId, decimal montoApertura)
    {
        ArgumentNullException.ThrowIfNull(caja);
        if (usuarioAperturaId == Guid.Empty)
            throw new DomainException("El usuario que abre la caja es obligatorio.");
        if (montoApertura < 0)
            throw new DomainException("El monto de apertura no puede ser negativo.");

        caja.MarcarComoAbierta();

        return new CajaSesion(Guid.NewGuid())
        {
            CajaId = caja.Id,
            UsuarioAperturaId = usuarioAperturaId,
            MontoApertura = montoApertura,
            FechaAperturaUtc = DateTime.UtcNow,
            EnCurso = true
        };
    }

    /// <summary>
    /// Acumula una venta cobrada en este turno. Al modificar la sesión, su RowVersion hace que
    /// una venta y un cierre simultáneos no puedan confirmarse ambos (uno recibe conflicto).
    /// </summary>
    public void RegistrarCobro(Venta venta)
    {
        ArgumentNullException.ThrowIfNull(venta);
        if (!EnCurso)
            throw new DomainException("No se puede vender en una caja cerrada.");
        if (venta.CajaSesionId != Id)
            throw new DomainException("La venta no pertenece a este turno de caja.");

        CantidadVentas++;
        TotalVendido += venta.TotalGeneral;
        MarcarModificado();
    }

    /// <summary>Acumula un recibo de cobranza (misma protección de concurrencia que las ventas).</summary>
    internal void RegistrarCobranza(ReciboCobranza recibo)
    {
        if (!EnCurso)
            throw new DomainException("No se puede cobrar en una caja cerrada.");
        if (recibo.CajaSesionId != Id)
            throw new DomainException("El recibo no pertenece a este turno de caja.");

        CantidadCobranzas++;
        TotalCobranzas += recibo.Monto;
        MarcarModificado();
    }

    /// <param name="totalCobradoEnEfectivo">Suma de lo cobrado en efectivo durante el turno (lo calcula la capa de aplicación).</param>
    public void Cerrar(Caja caja, Guid usuarioCierreId, decimal totalCobradoEnEfectivo, decimal montoCierreReal, string? observaciones)
    {
        ArgumentNullException.ThrowIfNull(caja);
        if (caja.Id != CajaId)
            throw new DomainException("La caja indicada no corresponde a esta sesión.");
        if (!EnCurso)
            throw new DomainException("La sesión de caja ya está cerrada.");
        if (usuarioCierreId == Guid.Empty)
            throw new DomainException("El usuario que cierra la caja es obligatorio.");
        if (totalCobradoEnEfectivo < 0 || montoCierreReal < 0)
            throw new DomainException("Los montos de cierre no pueden ser negativos.");

        caja.MarcarComoCerrada();

        UsuarioCierreId = usuarioCierreId;
        FechaCierreUtc = DateTime.UtcNow;
        MontoCierreSistema = MontoApertura + totalCobradoEnEfectivo;
        MontoCierreReal = montoCierreReal;
        DiferenciaArqueo = montoCierreReal - MontoCierreSistema;
        Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
        EnCurso = false;
        MarcarModificado();
    }
}
