using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Proveedor de mercadería del comercio. El CUIT es obligatorio y único dentro de cada comercio.
/// </summary>
public sealed class Proveedor : TenantEntity
{
    public const int LongitudMaximaRazonSocial = 150;
    public const int LongitudCuit = 11;
    public const int LongitudMaximaTelefono = 30;
    public const int LongitudMaximaEmail = 150;
    public const int LongitudMaximaDireccion = 200;

    public string RazonSocial { get; private set; } = string.Empty;

    /// <summary>CUIT normalizado: 11 dígitos sin guiones.</summary>
    public string Cuit { get; private set; } = string.Empty;

    public string? Telefono { get; private set; }
    public string? Email { get; private set; }
    public string? Direccion { get; private set; }
    public bool EstaActivo { get; private set; }

    private Proveedor(Guid id) : base(id) { }

    // Requerido por EF Core
    private Proveedor() { }

    public static Proveedor Crear(string razonSocial, string cuit, string? telefono, string? email, string? direccion)
    {
        var proveedor = new Proveedor(Guid.NewGuid()) { EstaActivo = true };
        proveedor.Actualizar(razonSocial, cuit, telefono, email, direccion);
        return proveedor;
    }

    public void Actualizar(string razonSocial, string cuit, string? telefono, string? email, string? direccion)
    {
        if (string.IsNullOrWhiteSpace(razonSocial))
            throw new DomainException("La razón social del proveedor es obligatoria.");

        RazonSocial = razonSocial.Trim();
        Cuit = NormalizarCuit(cuit);
        Telefono = Limpiar(telefono);
        Email = Limpiar(email)?.ToLowerInvariant();
        Direccion = Limpiar(direccion);
        MarcarModificado();
    }

    /// <summary>Deja solo los dígitos ("30-71234567-9" → "30712345679") y valida que sean 11.</summary>
    public static string NormalizarCuit(string? cuit)
    {
        var digitos = new string((cuit ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length != LongitudCuit)
            throw new DomainException("El CUIT del proveedor debe tener 11 dígitos.");
        return digitos;
    }

    public void Desactivar()
    {
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
