using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Seguridad;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Application.Features.Productos;

// ---------- Puerto: lectura y escritura de la planilla (implementado en Infrastructure) ----------

/// <summary>Convierte el catálogo a una planilla Excel (.xlsx) y lee una planilla subida por el usuario.</summary>
public interface IPlanillaProductos
{
    byte[] Exportar(IReadOnlyList<FilaExportacionProducto> productos);

    /// <summary>
    /// Lee la primera hoja. Las celdas con formato inválido (un texto en una columna numérica) se informan
    /// como errores de su fila. Lanza <see cref="PlanillaInvalidaException"/> si el archivo no es un .xlsx o no tiene la columna SKU.
    /// </summary>
    LecturaPlanillaProductos Leer(Stream archivo);
}

public sealed record FilaExportacionProducto(
    string Sku, string Nombre, string? CodigoBarras, string Categoria, decimal Costo, decimal Margen, decimal PrecioVenta,
    decimal Iva, decimal StockMinimo, bool VenderSinStock, decimal Stock, bool Activo);

/// <summary>Una fila leída. Una celda vacía llega como null: en un producto existente significa "no cambiar".</summary>
public sealed record FilaPlanillaProducto(
    int Fila, string? Sku, string? Nombre, string? CodigoBarras, string? Categoria, decimal? Costo, decimal? Margen,
    decimal? PrecioVenta, decimal? Iva, decimal? StockMinimo, bool? VenderSinStock, decimal? Stock, bool? Activo);

public sealed record LecturaPlanillaProductos(IReadOnlyList<FilaPlanillaProducto> Filas, IReadOnlyList<ErrorFilaImportacion> Errores);

public sealed record ErrorFilaImportacion(int Fila, string Mensaje);

public sealed record ArchivoGeneradoDto(byte[] Contenido, string TipoContenido);

public sealed record ResultadoImportacionDto(
    bool Aplicado,
    int FilasLeidas,
    int Nuevos,
    int Actualizados,
    int SinCambios,
    IReadOnlyList<string> CategoriasNuevas,
    IReadOnlyList<ErrorFilaImportacion> Errores);

// ---------- Exportación ----------

[RequierePermiso(Permisos.ProductosExportar)]
public sealed record ExportarProductosQuery : IRequest<ArchivoGeneradoDto>;

public sealed class ExportarProductosQueryHandler(IApplicationDbContext db, IPlanillaProductos planilla)
    : IRequestHandler<ExportarProductosQuery, ArchivoGeneradoDto>
{
    public const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<ArchivoGeneradoDto> Handle(ExportarProductosQuery request, CancellationToken ct)
    {
        // Incluye los dados de baja: la planilla es también el respaldo completo del catálogo.
        var productos = await db.Productos.AsNoTracking().Include(p => p.Categoria)
            .OrderBy(p => p.Categoria!.OrdenDisplay).ThenBy(p => p.Categoria!.Nombre).ThenBy(p => p.Nombre)
            .ToListAsync(ct);

        var filas = productos.Select(p => new FilaExportacionProducto(
            p.Sku.Value, p.Nombre, p.CodigoBarras, p.Categoria?.Nombre ?? string.Empty, p.PrecioCompra.Monto, p.PorcentajeGanancia,
            p.PrecioVenta.Monto, p.PorcentajeIva, p.StockMinimo, p.VenderSinStock, p.StockActual, p.EstaActivo)).ToList();

        return new ArchivoGeneradoDto(planilla.Exportar(filas), TipoXlsx);
    }
}

// ---------- Importación ----------

/// <summary>
/// Alta y actualización masiva por SKU. Es todo o nada: si alguna fila tiene errores no se guarda ninguna.
/// Con <paramref name="Simular"/> valida y devuelve el resumen sin guardar (vista previa).
/// El stock de un producto existente no se toca (solo cambia por compras y ventas); en uno nuevo, la columna Stock es el stock inicial.
/// </summary>
[RequierePermiso(Permisos.ProductosImportar)]
public sealed record ImportarProductosCommand(byte[] Contenido, bool Simular) : IRequest<ResultadoImportacionDto>;

public sealed class ImportarProductosCommandValidator : AbstractValidator<ImportarProductosCommand>
{
    public const int TamanioMaximo = 5 * 1024 * 1024;

    public ImportarProductosCommandValidator()
    {
        RuleFor(x => x.Contenido).NotEmpty().WithMessage("Elegí una planilla .xlsx.")
            .Must(c => c.Length <= TamanioMaximo).WithMessage("La planilla no puede superar los 5 MB.");
    }
}

public sealed class ImportarProductosCommandHandler(IApplicationDbContext db, IPlanillaProductos planilla)
    : IRequestHandler<ImportarProductosCommand, ResultadoImportacionDto>
{
    public const int FilasMaximas = 5_000;
    private const int ErroresMaximos = 200;

    public async Task<ResultadoImportacionDto> Handle(ImportarProductosCommand request, CancellationToken ct)
    {
        using var stream = new MemoryStream(request.Contenido, writable: false);
        var lectura = planilla.Leer(stream);
        if (lectura.Filas.Count + lectura.Errores.Select(e => e.Fila).Distinct().Count() > FilasMaximas)
            throw new PlanillaInvalidaException($"La planilla supera las {FilasMaximas:N0} filas. Dividila en varios archivos.");
        if (lectura.Filas.Count == 0 && lectura.Errores.Count == 0)
            throw new PlanillaInvalidaException("La planilla no tiene productos: completá al menos una fila debajo de los encabezados.");

        var estado = new EstadoImportacion(
            await db.Productos.ToListAsync(ct),
            await db.Categorias.ToListAsync(ct));
        var errores = new List<ErrorFilaImportacion>(lectura.Errores);

        foreach (var fila in lectura.Filas)
        {
            var error = Procesar(fila, estado);
            if (error is not null)
                errores.Add(new ErrorFilaImportacion(fila.Fila, error));
        }

        var aplicado = errores.Count == 0 && !request.Simular;
        if (aplicado)
            await db.SaveChangesAsync(ct); // un solo SaveChanges: todo o nada

        return new ResultadoImportacionDto(
            aplicado,
            lectura.Filas.Count + lectura.Errores.Select(e => e.Fila).Distinct().Count(),
            estado.Nuevos,
            estado.Actualizados,
            estado.SinCambios,
            estado.CategoriasNuevas.Select(c => c.Nombre).ToList(),
            errores.OrderBy(e => e.Fila).Take(ErroresMaximos).ToList());
    }

    /// <summary>Valida la fila y aplica los cambios a las entidades rastreadas. Devuelve el error, si lo hay.</summary>
    private string? Procesar(FilaPlanillaProducto f, EstadoImportacion s)
    {
        if (string.IsNullOrWhiteSpace(f.Sku))
            return "Falta el SKU.";

        Sku sku;
        try { sku = Sku.Crear(f.Sku); }
        catch (DomainException ex) { return ex.Message; }

        if (s.FilaPorSku.TryGetValue(sku.Value, out var filaAnterior))
            return $"El SKU '{sku}' está repetido (también en la fila {filaAnterior}).";
        s.FilaPorSku[sku.Value] = f.Fila;

        var existente = s.ProductoPorSku.GetValueOrDefault(sku.Value);

        var nombre = Limpiar(f.Nombre) ?? existente?.Nombre;
        if (nombre is null) return "Falta el nombre.";

        // Categoría: por nombre; si no existe se crea. Vacía en un producto existente = la que ya tiene.
        Categoria? categoria = null;
        var nombreCategoria = Limpiar(f.Categoria);
        if (nombreCategoria is not null)
        {
            if (nombreCategoria.Length > Categoria.LongitudMaximaNombre)
                return $"El nombre de la categoría no puede superar {Categoria.LongitudMaximaNombre} caracteres.";
            categoria = s.CategoriaPorNombre.GetValueOrDefault(nombreCategoria);
            if (categoria is { EstaActivo: false })
                return $"La categoría '{categoria.Nombre}' está dada de baja: reactivala o usá otra.";
        }
        else if (existente is null)
            return "Falta la categoría.";

        var costo = f.Costo ?? existente?.PrecioCompra.Monto;
        if (costo is null) return "Falta el costo.";
        var iva = f.Iva ?? existente?.PorcentajeIva;
        if (iva is null) return "Falta el IVA %.";
        var margen = f.Margen ?? existente?.PorcentajeGanancia ?? 0m;
        var stockMinimo = f.StockMinimo ?? existente?.StockMinimo ?? 0m;
        var venderSinStock = f.VenderSinStock ?? existente?.VenderSinStock ?? false;
        var activo = f.Activo ?? existente?.EstaActivo ?? true;
        var codigoBarras = f.CodigoBarras is null ? existente?.CodigoBarras : Producto.NormalizarCodigoBarras(f.CodigoBarras);

        // El precio de venta manda si es nuevo o si se lo cambió; si quedó igual al exportado, el precio sale del costo y el margen.
        decimal? precioFijo = f.PrecioVenta is { } pv && (existente is null || pv != existente.PrecioVenta.Monto) ? pv : null;

        var validacion = new DatosProductoValidator().Validate(new DatosImportados(
            sku.Value, nombre, codigoBarras, costo.Value, margen, precioFijo, iva.Value,
            categoria?.Id ?? existente?.CategoriaId ?? Guid.NewGuid(), stockMinimo));
        if (!validacion.IsValid)
            return string.Join(" ", validacion.Errors.Select(e => e.ErrorMessage).Distinct());

        if (existente is null && f.Stock is { } stock && stock is < 0 or > 1_000_000)
            return "El stock inicial debe estar entre 0 y 1.000.000.";

        if (codigoBarras is not null)
        {
            if (s.SkuPorCodigo.TryGetValue(codigoBarras, out var duenio) && duenio != sku.Value)
                return $"El código de barras {codigoBarras} ya lo usa el producto '{duenio}'.";
        }

        try
        {
            if (existente is null)
                Crear(s, sku, nombre, codigoBarras, categoria ?? CrearCategoria(s, nombreCategoria!), costo.Value, margen, precioFijo,
                    iva.Value, stockMinimo, venderSinStock, activo, f.Stock ?? 0m);
            else
                Actualizar(s, existente, nombre, codigoBarras, nombreCategoria is null ? null : categoria ?? CrearCategoria(s, nombreCategoria),
                    costo.Value, margen, precioFijo, iva.Value, stockMinimo, venderSinStock, activo);
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }

        if (codigoBarras is not null)
            s.SkuPorCodigo[codigoBarras] = sku.Value;
        return null;
    }

    private void Crear(
        EstadoImportacion s, Sku sku, string nombre, string? codigoBarras, Categoria categoria, decimal costo, decimal margen,
        decimal? precioFijo, decimal iva, decimal stockMinimo, bool venderSinStock, bool activo, decimal stockInicial)
    {
        var precioCompra = Money.Crear(costo, null);
        var producto = Producto.Crear(sku, nombre, precioCompra, margen, iva, categoria.Id, venderSinStock, stockMinimo);
        producto.AsignarCodigoBarras(codigoBarras);
        if (precioFijo.HasValue)
            ReglasProducto.AplicarPrecios(producto, precioCompra, margen, precioFijo);
        if (stockInicial > 0)
            producto.IngresarStock(stockInicial);
        if (!activo)
            producto.Desactivar();

        db.Productos.Add(producto);
        s.ProductoPorSku[sku.Value] = producto;
        s.Nuevos++;
    }

    private static void Actualizar(
        EstadoImportacion s, Producto p, string nombre, string? codigoBarras, Categoria? categoria, decimal costo, decimal margen,
        decimal? precioFijo, decimal iva, decimal stockMinimo, bool venderSinStock, bool activo)
    {
        var cambiaPrecio = precioFijo.HasValue || costo != p.PrecioCompra.Monto || margen != p.PorcentajeGanancia;
        var cambia = cambiaPrecio || nombre != p.Nombre || iva != p.PorcentajeIva || codigoBarras != p.CodigoBarras
            || (categoria is not null && categoria.Id != p.CategoriaId) || stockMinimo != p.StockMinimo
            || venderSinStock != p.VenderSinStock || activo != p.EstaActivo;
        if (!cambia)
        {
            s.SinCambios++;
            return;
        }

        p.ActualizarDatosBasicos(p.Sku, nombre, iva);
        p.AsignarCodigoBarras(codigoBarras);
        if (categoria is not null)
            p.CambiarCategoria(categoria.Id);
        if (cambiaPrecio)
            ReglasProducto.AplicarPrecios(p, Money.Crear(costo, p.PrecioCompra.Moneda), margen, precioFijo);
        p.ConfigurarVentaSinStock(venderSinStock);
        p.ConfigurarStockMinimo(stockMinimo);
        if (activo) p.Activar(); else p.Desactivar();
        s.Actualizados++;
    }

    private Categoria CrearCategoria(EstadoImportacion s, string nombre)
    {
        s.UltimoOrden += 10;
        var categoria = Categoria.Crear(nombre, null, s.UltimoOrden);
        db.Categorias.Add(categoria);
        s.CategoriaPorNombre[categoria.Nombre] = categoria;
        s.CategoriasNuevas.Add(categoria);
        return categoria;
    }

    private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private sealed record DatosImportados(
        string Sku, string Nombre, string? CodigoBarras, decimal PrecioCompra, decimal PorcentajeGanancia, decimal? PrecioVenta,
        decimal PorcentajeIva, Guid CategoriaId, decimal StockMinimo) : IDatosProducto;

    private sealed class EstadoImportacion
    {
        public EstadoImportacion(List<Producto> productos, List<Categoria> categorias)
        {
            ProductoPorSku = productos.ToDictionary(p => p.Sku.Value);
            SkuPorCodigo = productos.Where(p => p.CodigoBarras is not null).ToDictionary(p => p.CodigoBarras!, p => p.Sku.Value);
            CategoriaPorNombre = new Dictionary<string, Categoria>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in categorias.OrderByDescending(c => c.EstaActivo)) // si hay homónimas, gana la activa
                CategoriaPorNombre.TryAdd(c.Nombre, c);
            UltimoOrden = categorias.Count == 0 ? 0 : categorias.Max(c => c.OrdenDisplay);
        }

        public Dictionary<string, Producto> ProductoPorSku { get; }
        public Dictionary<string, string> SkuPorCodigo { get; }
        public Dictionary<string, Categoria> CategoriaPorNombre { get; }
        public Dictionary<string, int> FilaPorSku { get; } = new();
        public List<Categoria> CategoriasNuevas { get; } = [];
        public int UltimoOrden { get; set; }
        public int Nuevos { get; set; }
        public int Actualizados { get; set; }
        public int SinCambios { get; set; }
    }
}
