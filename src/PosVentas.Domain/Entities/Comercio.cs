using System.Text.RegularExpressions;
using PosVentas.Domain.Common;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Comercio cliente del SaaS. Es el tenant: su Id es el TenantId del resto de las entidades.
/// </summary>
public sealed partial class Comercio : Entity<Guid>
{
    public const int LongitudMaximaNombre = 150;
    public const int LongitudMaximaSlug = 50;

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Identificador público y único del comercio (se usa en el login). Ej: "almacen-don-jose".</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Cuit { get; private set; }
    public bool EstaActivo { get; private set; }

    private Comercio(Guid id, string nombre, string slug, string? cuit) : base(id)
    {
        Nombre = nombre;
        Slug = slug;
        Cuit = cuit;
        EstaActivo = true;
    }

    // Requerido por EF Core
    private Comercio() { }

    public static Comercio Crear(string nombre, string slug, string? cuit)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new DomainException("El nombre del comercio es obligatorio.");

        var slugNormalizado = NormalizarSlug(slug);

        return new Comercio(Guid.NewGuid(), nombre.Trim(), slugNormalizado,
            string.IsNullOrWhiteSpace(cuit) ? null : cuit.Trim());
    }

    public static string NormalizarSlug(string? slug)
    {
        var valor = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!SlugRegex().IsMatch(valor))
            throw new DomainException("El identificador del comercio debe tener entre 3 y 50 caracteres: letras minúsculas, números y guiones.");
        return valor;
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

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$")]
    private static partial Regex SlugRegex();
}
