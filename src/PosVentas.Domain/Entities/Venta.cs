using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Comprobante de venta. Recalcula sus totales fiscales (neto gravado, no gravado, IVA) al agregar ítems.
/// Las claves foráneas a Cliente, Usuario y CajaSesion son explícitas y obligatorias.
/// </summary>
public sealed class Venta : TenantEntity
{
    public const int LongitudMaximaCae = 20;

    public DateTime FechaUtc { get; private set; }

    public Guid ClienteId { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid CajaSesionId { get; private set; }

    public decimal TotalNetoNoGravado { get; private set; }
    public decimal TotalNetoGravado { get; private set; }
    public decimal TotalIva { get; private set; }
    public decimal TotalGeneral { get; private set; }

    // --- Preparado para facturación electrónica (ARCA / AFIP) ---
    public TipoComprobante TipoComprobante { get; private set; }
    public int? PuntoVenta { get; private set; }
    public long? NumeroComprobante { get; private set; }
    public string? Cae { get; private set; }
    public DateTime? VencimientoCae { get; private set; }
    public EstadoFacturacion EstadoFacturacion { get; private set; }
    public bool EsFiscal => !string.IsNullOrEmpty(Cae);

    /// <summary>Vuelto entregado en efectivo (lo pagado por encima del total).</summary>
    public decimal Vuelto { get; private set; }

    public decimal TotalPagado => _pagos.Sum(p => p.Monto);

    /// <summary>Parte de la venta cargada a la cuenta corriente del cliente.</summary>
    public decimal MontoCuentaCorriente => _pagos.Where(p => p.Medio == MedioPago.CuentaCorriente).Sum(p => p.Monto);

    private readonly List<VentaDetalle> _detalles = [];
    public IReadOnlyCollection<VentaDetalle> Detalles => _detalles.AsReadOnly();

    private readonly List<VentaPago> _pagos = [];
    public IReadOnlyCollection<VentaPago> Pagos => _pagos.AsReadOnly();

    private Venta(Guid id) : base(id) { }

    // Requerido por EF Core
    private Venta() { }

    public static Venta Iniciar(Guid clienteId, Guid usuarioId, CajaSesion cajaSesion, TipoComprobante tipoComprobante)
    {
        ArgumentNullException.ThrowIfNull(cajaSesion);
        if (clienteId == Guid.Empty)
            throw new DomainException("El cliente es obligatorio.");
        if (usuarioId == Guid.Empty)
            throw new DomainException("El usuario es obligatorio.");
        if (!cajaSesion.EnCurso)
            throw new DomainException("No se puede vender en una caja cerrada.");
        if (tipoComprobante != TipoComprobante.Ticket && !Fiscal.TiposComprobanteAfip.EsFactura(tipoComprobante))
            throw new DomainException("Una venta se emite como ticket interno o como factura A, B o C.");

        return new Venta(Guid.NewGuid())
        {
            FechaUtc = DateTime.UtcNow,
            ClienteId = clienteId,
            UsuarioId = usuarioId,
            CajaSesionId = cajaSesion.Id,
            TipoComprobante = tipoComprobante,
            EstadoFacturacion = tipoComprobante == TipoComprobante.Ticket
                ? EstadoFacturacion.NoRequerida
                : EstadoFacturacion.Pendiente
        };
    }

    /// <summary>
    /// Marca la venta para facturar con el tipo indicado: un ticket interno que se factura a posteriori,
    /// o una factura pendiente que se reintenta (se puede cambiar la letra, p. ej. de A a B).
    /// </summary>
    public void SolicitarFacturacion(TipoComprobante tipo)
    {
        if (EstadoFacturacion == EstadoFacturacion.Facturada)
            throw new DomainException("La venta ya está facturada.");
        if (!Fiscal.TiposComprobanteAfip.EsFactura(tipo))
            throw new DomainException("Solo se puede facturar como factura A, B o C.");

        TipoComprobante = tipo;
        EstadoFacturacion = EstadoFacturacion.Pendiente;
        MarcarModificado();
    }

    /// <summary>Agrega un renglón y descuenta el stock del producto mediante su método de dominio.</summary>
    public void AgregarItem(Producto producto, decimal cantidad)
    {
        ArgumentNullException.ThrowIfNull(producto);
        if (EsFiscal)
            throw new DomainException("No se pueden modificar ventas con CAE asignado.");
        if (_pagos.Count > 0)
            throw new DomainException("No se pueden agregar ítems a una venta ya cobrada.");

        producto.DescontarStock(cantidad);

        _detalles.Add(VentaDetalle.Crear(Id, producto, cantidad));
        RecalcularTotales();
    }

    public void RegistrarAprobacionFiscal(int puntoVenta, long numeroComprobante, string cae, DateTime vencimientoCae)
    {
        if (TipoComprobante == TipoComprobante.Ticket)
            throw new DomainException("Un ticket interno no lleva CAE.");
        if (EstadoFacturacion != EstadoFacturacion.Pendiente)
            throw new DomainException("La venta no tiene una facturación pendiente.");
        if (string.IsNullOrWhiteSpace(cae))
            throw new DomainException("El CAE informado no es válido.");
        if (puntoVenta <= 0 || numeroComprobante <= 0)
            throw new DomainException("Punto de venta y número de comprobante deben ser positivos.");

        PuntoVenta = puntoVenta;
        NumeroComprobante = numeroComprobante;
        Cae = cae.Trim();
        VencimientoCae = vencimientoCae;
        EstadoFacturacion = EstadoFacturacion.Facturada;
        MarcarModificado();
    }

    /// <summary>
    /// Registra el cobro. Reglas:
    /// - lo pagado debe cubrir el total;
    /// - solo el efectivo admite excedente (se devuelve como vuelto);
    /// - los medios electrónicos y la cuenta corriente no pueden superar el total de la venta;
    /// - la cuenta corriente exige que el cliente de la venta la tenga habilitada y con crédito suficiente.
    /// </summary>
    public void RegistrarPagos(IEnumerable<PagoSolicitado> pagos, Cliente cliente)
    {
        ArgumentNullException.ThrowIfNull(pagos);
        ArgumentNullException.ThrowIfNull(cliente);
        if (cliente.Id != ClienteId)
            throw new DomainException("El cliente indicado no corresponde a la venta.");
        if (_detalles.Count == 0)
            throw new DomainException("La venta debe tener al menos un ítem.");
        if (_pagos.Count > 0)
            throw new DomainException("La venta ya tiene pagos registrados.");

        var lista = pagos.ToList();
        if (lista.Count == 0)
            throw new DomainException("Debe indicar al menos un medio de pago.");

        foreach (var pago in lista)
        {
            if (!Enum.IsDefined(pago.Medio))
                throw new DomainException("El medio de pago no es válido.");
            if (pago.Monto <= 0)
                throw new DomainException("Cada pago debe ser mayor a cero.");
        }

        // Un renglón por medio de pago
        var agrupados = lista
            .GroupBy(p => p.Medio)
            .Select(g => (Medio: g.Key, Monto: decimal.Round(g.Sum(p => p.Monto), 2, MidpointRounding.AwayFromZero)))
            .ToList();

        var totalPagado = agrupados.Sum(p => p.Monto);
        if (totalPagado < TotalGeneral)
            throw new DomainException($"El pago ({totalPagado:0.00}) no cubre el total de la venta ({TotalGeneral:0.00}).");

        var noEfectivo = agrupados.Where(p => p.Medio != MedioPago.Efectivo).Sum(p => p.Monto);
        if (noEfectivo > TotalGeneral)
            throw new DomainException("Los pagos con tarjeta, transferencia, QR o cuenta corriente no pueden superar el total: el vuelto solo se da en efectivo.");

        var aCuentaCorriente = agrupados.Where(p => p.Medio == MedioPago.CuentaCorriente).Sum(p => p.Monto);
        if (aCuentaCorriente > 0)
        {
            if (cliente.EsConsumidorFinalGenerico)
                throw new CreditoRechazadoException("Para vender a cuenta corriente hay que identificar al cliente.");
            cliente.VerificarCredito(aCuentaCorriente);
        }

        foreach (var (medio, monto) in agrupados)
            _pagos.Add(VentaPago.Crear(Id, medio, monto));

        Vuelto = totalPagado - TotalGeneral;
    }

    public void ValidarParaRegistrar()
    {
        if (_detalles.Count == 0)
            throw new DomainException("La venta debe tener al menos un ítem.");
        if (_pagos.Count == 0)
            throw new DomainException("La venta debe estar cobrada antes de registrarse.");
    }

    private void RecalcularTotales()
    {
        TotalNetoGravado = _detalles.Where(d => d.PorcentajeIva > 0).Sum(d => d.Subtotal);
        TotalNetoNoGravado = _detalles.Where(d => d.PorcentajeIva == 0).Sum(d => d.Subtotal);
        TotalIva = _detalles.Sum(d => d.MontoIva);
        TotalGeneral = TotalNetoGravado + TotalNetoNoGravado + TotalIva;
    }
}
