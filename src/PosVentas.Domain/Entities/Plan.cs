using System.Text.RegularExpressions;
using PosVentas.Domain.Common;
using PosVentas.Domain.Suscripciones;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Plan comercial del SaaS: precio mensual, límites y módulos incluidos. Lo administra HS Tech (administradores
/// de la plataforma); no pertenece a ningún comercio. El <see cref="Codigo"/> es estable y no se edita.
/// </summary>
public sealed partial class Plan : Entity<Guid>
{
    public const int LongitudMaximaCodigo = 30;
    public const int LongitudMaximaNombre = 60;
    public const int LongitudMaximaDescripcion = 300;
    public const decimal PrecioMaximo = 100_000_000m;
    public const int LimiteMaximo = 1_000_000;

    private readonly List<PlanModulo> _modulos = [];

    public string Codigo { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string? Descripcion { get; private set; }

    /// <summary>Precio mensual final en pesos.</summary>
    public decimal PrecioMensual { get; private set; }

    /// <summary>Usuarios activos permitidos; null = sin límite.</summary>
    public int? MaxUsuarios { get; private set; }

    /// <summary>Productos activos permitidos; null = sin límite.</summary>
    public int? MaxProductos { get; private set; }

    /// <summary>Orden de presentación (de menor a mayor).</summary>
    public int Orden { get; private set; }

    /// <summary>Se ofrece a los comercios. Uno inactivo sigue vigente para quien ya lo tiene.</summary>
    public bool EstaActivo { get; private set; }

    /// <summary>Plan con el que arranca la prueba gratuita de un comercio nuevo. Hay uno solo.</summary>
    public bool EsPredeterminado { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<PlanModulo> Modulos => _modulos.AsReadOnly();

    private Plan(Guid id) : base(id) { }

    // Requerido por EF Core
    private Plan() { }

    public static Plan Crear(string codigo, string nombre, string? descripcion, decimal precioMensual,
        int? maxUsuarios, int? maxProductos, IEnumerable<string> modulos, int orden)
    {
        var valor = (codigo ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodigoRegex().IsMatch(valor))
            throw new DomainException("El código del plan debe tener 3 a 30 caracteres: mayúsculas, números y guiones bajos.");

        var plan = new Plan(Guid.NewGuid()) { Codigo = valor, EstaActivo = true };
        plan.Modificar(nombre, descripcion, precioMensual, maxUsuarios, maxProductos, modulos, orden);
        return plan;
    }

    public void Modificar(string nombre, string? descripcion, decimal precioMensual,
        int? maxUsuarios, int? maxProductos, IEnumerable<string> modulos, int orden)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > LongitudMaximaNombre)
            throw new DomainException($"El nombre del plan es obligatorio (hasta {LongitudMaximaNombre} caracteres).");
        if (precioMensual is < 0 or > PrecioMaximo || decimal.Round(precioMensual, 2) != precioMensual)
            throw new DomainException("El precio mensual debe ser un importe válido, con hasta 2 decimales.");
        ValidarLimite(maxUsuarios, "usuarios");
        ValidarLimite(maxProductos, "productos");

        var codigos = modulos.Select(m => m?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
        var invalido = codigos.FirstOrDefault(m => !ModulosPlan.Existe(m));
        if (invalido is not null)
            throw new DomainException($"El módulo '{invalido}' no existe.");

        Nombre = nombre.Trim();
        Descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        PrecioMensual = precioMensual;
        MaxUsuarios = maxUsuarios;
        MaxProductos = maxProductos;
        Orden = orden;

        _modulos.RemoveAll(m => !codigos.Contains(m.Codigo));
        foreach (var codigo in codigos.Where(c => _modulos.All(m => m.Codigo != c)))
            _modulos.Add(new PlanModulo(codigo));

        MarcarModificado();
    }

    public bool Incluye(string modulo) => _modulos.Any(m => m.Codigo == modulo);

    public void Activar()
    {
        EstaActivo = true;
        MarcarModificado();
    }

    public void Desactivar()
    {
        if (EsPredeterminado)
            throw new DomainException("El plan predeterminado no se puede desactivar: elegí otro como predeterminado primero.");
        EstaActivo = false;
        MarcarModificado();
    }

    public void MarcarPredeterminado()
    {
        if (!EstaActivo)
            throw new DomainException("Solo un plan activo puede ser el predeterminado.");
        EsPredeterminado = true;
        MarcarModificado();
    }

    public void QuitarPredeterminado()
    {
        EsPredeterminado = false;
        MarcarModificado();
    }

    private static void ValidarLimite(int? limite, string que)
    {
        if (limite is < 1 or > LimiteMaximo)
            throw new DomainException($"El límite de {que} debe ser al menos 1 (o dejalo vacío para no limitar).");
    }

    [GeneratedRegex("^[A-Z0-9_]{3,30}$")]
    private static partial Regex CodigoRegex();
}

/// <summary>Módulo incluido en un plan (código de <see cref="ModulosPlan"/>).</summary>
public sealed class PlanModulo
{
    public string Codigo { get; private set; } = string.Empty;

    internal PlanModulo(string codigo) => Codigo = codigo;

    // Requerido por EF Core
    private PlanModulo() { }
}
