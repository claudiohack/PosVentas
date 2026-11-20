using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Entities;

public sealed class Cliente : TenantEntity
{
    public const int LongitudMaximaRazonSocial = 150;
    public const int LongitudMaximaDocumento = 20;

    public string RazonSocial { get; private set; } = string.Empty;
    public string? Documento { get; private set; }
    public CondicionIva CondicionIva { get; private set; }
    public string? Telefono { get; private set; }
    public string? Email { get; private set; }
    public string? Direccion { get; private set; }

    /// <summary>Cliente genérico "Consumidor Final" que se crea con cada comercio.</summary>
    public bool EsConsumidorFinalGenerico { get; private set; }

    public bool PermiteCuentaCorriente { get; private set; }
    public decimal LimiteCredito { get; private set; }

    /// <summary>Deuda actual del cliente en cuenta corriente (nunca negativa).</summary>
    public decimal SaldoActual { get; private set; }

    public decimal CreditoDisponible => PermiteCuentaCorriente ? Math.Max(0, LimiteCredito - SaldoActual) : 0m;

    public bool EstaActivo { get; private set; }

    /// <summary>Concurrencia optimista: dos ventas a crédito simultáneas no pueden superar juntas el límite.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private Cliente(Guid id) : base(id) { }

    // Requerido por EF Core
    private Cliente() { }

    public static Cliente Crear(
        string razonSocial,
        string? documento,
        CondicionIva condicionIva,
        string? telefono,
        string? email,
        string? direccion,
        bool permiteCuentaCorriente,
        decimal limiteCredito)
    {
        var cliente = new Cliente(Guid.NewGuid()) { EstaActivo = true };
        cliente.ActualizarDatos(razonSocial, documento, condicionIva, telefono, email, direccion);
        cliente.ConfigurarCredito(permiteCuentaCorriente, limiteCredito);
        return cliente;
    }

    public static Cliente CrearConsumidorFinalGenerico()
    {
        var cliente = Crear("Consumidor Final", null, CondicionIva.ConsumidorFinal, null, null, null, false, 0m);
        cliente.EsConsumidorFinalGenerico = true;
        return cliente;
    }

    public void ActualizarDatos(string razonSocial, string? documento, CondicionIva condicionIva,
        string? telefono, string? email, string? direccion)
    {
        if (string.IsNullOrWhiteSpace(razonSocial))
            throw new DomainException("La razón social del cliente es obligatoria.");
        if (!Enum.IsDefined(condicionIva))
            throw new DomainException("La condición frente al IVA no es válida.");

        RazonSocial = razonSocial.Trim();
        Documento = Limpiar(documento);
        CondicionIva = condicionIva;
        Telefono = Limpiar(telefono);
        Email = Limpiar(email);
        Direccion = Limpiar(direccion);
        MarcarModificado();
    }

    public void ConfigurarCredito(bool permiteCuentaCorriente, decimal limiteCredito)
    {
        if (limiteCredito < 0)
            throw new DomainException("El límite de crédito no puede ser negativo.");
        if (permiteCuentaCorriente && EsConsumidorFinalGenerico)
            throw new DomainException("El cliente Consumidor Final no puede operar en cuenta corriente.");
        if (!permiteCuentaCorriente && SaldoActual > 0)
            throw new DomainException("No se puede deshabilitar la cuenta corriente de un cliente con saldo pendiente.");

        // Bajar el límite por debajo del saldo es válido: solo bloquea nuevos cargos hasta que pague.
        PermiteCuentaCorriente = permiteCuentaCorriente;
        LimiteCredito = permiteCuentaCorriente ? limiteCredito : 0m;
        MarcarModificado();
    }

    /// <summary>Verifica que el cliente pueda tomar este cargo a crédito. Lanza <see cref="CreditoRechazadoException"/> si no.</summary>
    public void VerificarCredito(decimal monto)
    {
        if (monto <= 0)
            throw new DomainException("El importe a cuenta corriente debe ser mayor a cero.");
        if (!EstaActivo)
            throw new CreditoRechazadoException($"El cliente '{RazonSocial}' está dado de baja.");
        if (!PermiteCuentaCorriente)
            throw new CreditoRechazadoException($"El cliente '{RazonSocial}' no tiene habilitada la cuenta corriente.");
        if (SaldoActual + monto > LimiteCredito)
            throw new CreditoRechazadoException(
                $"Límite de crédito excedido para '{RazonSocial}': saldo {SaldoActual:0.00} + cargo {monto:0.00} supera el límite de {LimiteCredito:0.00} (disponible {CreditoDisponible:0.00}).");
    }

    /// <summary>Carga una venta a crédito y devuelve el asiento de la cuenta corriente.</summary>
    public MovimientoCuentaCorriente RegistrarCargo(decimal monto, Guid ventaId, Guid usuarioId)
    {
        VerificarCredito(monto);

        SaldoActual += monto;
        MarcarModificado();

        return MovimientoCuentaCorriente.Crear(Id, TipoMovimientoCuentaCorriente.Cargo, monto, SaldoActual,
            ventaId, null, usuarioId, "Venta a cuenta corriente");
    }

    /// <summary>Acredita un pago y devuelve el asiento de la cuenta corriente.</summary>
    internal MovimientoCuentaCorriente RegistrarAbono(decimal monto, Guid reciboId, Guid usuarioId)
    {
        if (monto <= 0)
            throw new DomainException("El importe cobrado debe ser mayor a cero.");
        if (monto > SaldoActual)
            throw new DomainException($"El importe cobrado ({monto:0.00}) supera la deuda del cliente ({SaldoActual:0.00}).");

        SaldoActual -= monto;
        MarcarModificado();

        return MovimientoCuentaCorriente.Crear(Id, TipoMovimientoCuentaCorriente.Abono, monto, SaldoActual,
            null, reciboId, usuarioId, "Recibo de cobranza");
    }

    /// <summary>
    /// Revierte el cargo de una venta anulada. Si el cliente ya había pagado parte de esa deuda,
    /// el saldo puede quedar negativo: es saldo a favor del cliente.
    /// </summary>
    internal MovimientoCuentaCorriente RevertirCargo(decimal monto, Guid ventaId, Guid usuarioId)
    {
        if (monto <= 0)
            throw new DomainException("El importe a acreditar debe ser mayor a cero.");

        SaldoActual -= monto;
        MarcarModificado();

        return MovimientoCuentaCorriente.Crear(Id, TipoMovimientoCuentaCorriente.Abono, monto, SaldoActual,
            ventaId, null, usuarioId, "Anulación de venta a cuenta corriente");
    }

    public void Desactivar()
    {
        if (EsConsumidorFinalGenerico)
            throw new DomainException("No se puede dar de baja el cliente Consumidor Final.");
        if (SaldoActual > 0)
            throw new DomainException("No se puede dar de baja un cliente con saldo pendiente.");
        EstaActivo = false;
        MarcarModificado();
    }

    public void Activar()
    {
        EstaActivo = true;
        MarcarModificado();
    }

    private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
