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
    public bool EstaActivo { get; private set; }

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

        PermiteCuentaCorriente = permiteCuentaCorriente;
        LimiteCredito = permiteCuentaCorriente ? limiteCredito : 0m;
        MarcarModificado();
    }

    public void Desactivar()
    {
        if (EsConsumidorFinalGenerico)
            throw new DomainException("No se puede dar de baja el cliente Consumidor Final.");
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
