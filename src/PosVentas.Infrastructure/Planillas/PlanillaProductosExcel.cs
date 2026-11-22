using System.Globalization;
using System.Text;
using PosVentas.Application.Common.Exceptions;
using PosVentas.Application.Features.Productos;

namespace PosVentas.Infrastructure.Planillas;

/// <summary>
/// Formato de la planilla de productos. Las columnas se reconocen por su encabezado (sin importar el orden,
/// mayúsculas ni tildes), así una planilla armada a mano también sirve.
/// </summary>
public sealed class PlanillaProductosExcel : IPlanillaProductos
{
    public static readonly string[] Encabezados =
    [
        "SKU", "Nombre", "Código de barras", "Categoría", "Costo", "Margen %", "Precio venta (neto)", "IVA %",
        "Stock mínimo", "Vender sin stock", "Stock", "Activo"
    ];

    private enum Columna { Sku, Nombre, CodigoBarras, Categoria, Costo, Margen, PrecioVenta, Iva, StockMinimo, VenderSinStock, Stock, Activo }

    private static readonly Dictionary<string, Columna> Alias = new()
    {
        ["sku"] = Columna.Sku, ["codigointerno"] = Columna.Sku,
        ["nombre"] = Columna.Nombre, ["descripcion"] = Columna.Nombre, ["producto"] = Columna.Nombre,
        ["codigodebarras"] = Columna.CodigoBarras, ["codigobarras"] = Columna.CodigoBarras, ["ean"] = Columna.CodigoBarras,
        ["categoria"] = Columna.Categoria, ["rubro"] = Columna.Categoria,
        ["costo"] = Columna.Costo, ["preciocompra"] = Columna.Costo, ["preciodecompra"] = Columna.Costo,
        ["margen"] = Columna.Margen, ["ganancia"] = Columna.Margen, ["porcentajeganancia"] = Columna.Margen,
        ["precioventa"] = Columna.PrecioVenta, ["preciodeventa"] = Columna.PrecioVenta, ["precioventaneto"] = Columna.PrecioVenta,
        ["precio"] = Columna.PrecioVenta, ["precioneto"] = Columna.PrecioVenta,
        ["iva"] = Columna.Iva, ["alicuotaiva"] = Columna.Iva,
        ["stockminimo"] = Columna.StockMinimo, ["minimo"] = Columna.StockMinimo,
        ["vendersinstock"] = Columna.VenderSinStock,
        ["stock"] = Columna.Stock, ["stockinicial"] = Columna.Stock, ["stockactual"] = Columna.Stock,
        ["activo"] = Columna.Activo, ["estado"] = Columna.Activo
    };

    private static readonly CultureInfo Argentina = CultureInfo.GetCultureInfo("es-AR");

    public byte[] Exportar(IReadOnlyList<FilaExportacionProducto> productos)
    {
        var filas = productos.Select(p => new object?[]
        {
            p.Sku, p.Nombre, p.CodigoBarras, p.Categoria, p.Costo, p.Margen, p.PrecioVenta, p.Iva,
            new CantidadExcel(p.StockMinimo), p.VenderSinStock, new CantidadExcel(p.Stock), p.Activo
        });

        return LibroExcel.Escribir(
            new HojaExcel("Productos", Encabezados, filas, [16, 40, 18, 20, 13, 11, 18, 9, 13, 16, 11, 9]),
            new HojaExcel("Instrucciones", ["Cómo usar esta planilla"], Instrucciones.Select(t => new object?[] { t }), [120]));
    }

    private static readonly string[] Instrucciones =
    [
        "Cada fila es un producto y se identifica por el SKU: si el SKU ya existe se actualiza; si no, se crea.",
        "Para un producto nuevo son obligatorios: SKU, Nombre, Categoría, Costo e IVA %.",
        "En un producto existente, una celda vacía deja el valor que ya tiene.",
        "Si la categoría no existe se crea automáticamente.",
        "Precio: si cambiás el 'Precio venta (neto)', ese precio queda fijo y el margen se recalcula. Si lo dejás igual, el precio sale del costo y el margen.",
        "IVA %: 0; 2,5; 5; 10,5; 21 o 27 (como número, sin el signo %).",
        "Stock: solo se usa como stock inicial de los productos nuevos. El stock de los existentes no cambia (se mueve con compras y ventas).",
        "Vender sin stock y Activo: Sí o No.",
        "La importación es todo o nada: si una fila tiene un error no se guarda ninguna. Primero se muestra una vista previa.",
        "Máximo 5.000 filas y 5 MB por archivo."
    ];

    public LecturaPlanillaProductos Leer(Stream archivo)
    {
        var filas = LibroExcel.LeerPrimeraHoja(archivo);
        if (filas.Count == 0)
            throw new PlanillaInvalidaException("La planilla está vacía.");

        var (numeroEncabezado, encabezado) = filas[0];
        var columnas = new Dictionary<Columna, int>();
        for (var i = 0; i < encabezado.Length; i++)
        {
            if (encabezado[i] is { } celda && Alias.TryGetValue(Normalizar(celda.Texto), out var columna))
                columnas.TryAdd(columna, i);
        }
        if (!columnas.ContainsKey(Columna.Sku))
            throw new PlanillaInvalidaException($"No se encontró la columna SKU en la fila {numeroEncabezado}. Usá como base la planilla exportada.");

        var resultado = new List<FilaPlanillaProducto>();
        var errores = new List<ErrorFilaImportacion>();
        foreach (var (numero, celdas) in filas.Skip(1))
        {
            var lector = new LectorFila(celdas, columnas);
            var fila = new FilaPlanillaProducto(
                numero,
                lector.Texto(Columna.Sku),
                lector.Texto(Columna.Nombre),
                lector.Texto(Columna.CodigoBarras),
                lector.Texto(Columna.Categoria),
                lector.Numero(Columna.Costo, "Costo"),
                lector.Numero(Columna.Margen, "Margen %"),
                lector.Numero(Columna.PrecioVenta, "Precio venta"),
                lector.Numero(Columna.Iva, "IVA %"),
                lector.Numero(Columna.StockMinimo, "Stock mínimo"),
                lector.SiNo(Columna.VenderSinStock, "Vender sin stock"),
                lector.Numero(Columna.Stock, "Stock"),
                lector.SiNo(Columna.Activo, "Activo"));

            if (lector.Errores.Count > 0)
                errores.Add(new ErrorFilaImportacion(numero, string.Join(" ", lector.Errores)));
            else if (!lector.EstaVacia)
                resultado.Add(fila);
        }
        return new LecturaPlanillaProductos(resultado, errores);
    }

    private static string Normalizar(string texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private sealed class LectorFila(CeldaExcel?[] celdas, Dictionary<Columna, int> columnas)
    {
        public List<string> Errores { get; } = [];
        public bool EstaVacia { get; private set; } = true;

        private CeldaExcel? Celda(Columna columna)
        {
            if (!columnas.TryGetValue(columna, out var i) || i >= celdas.Length || celdas[i] is not { } celda || string.IsNullOrWhiteSpace(celda.Texto))
                return null;
            EstaVacia = false;
            return celda;
        }

        public string? Texto(Columna columna)
        {
            if (Celda(columna) is not { } celda) return null;
            // Un código guardado como número (7790000000011) puede venir como 7.790000000011E+12
            if (celda.EsNumero && decimal.TryParse(celda.Texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == decimal.Truncate(n))
                return n.ToString("0", CultureInfo.InvariantCulture);
            return celda.Texto.Trim();
        }

        public decimal? Numero(Columna columna, string nombre)
        {
            if (Celda(columna) is not { } celda) return null;
            var texto = celda.Texto.Trim().TrimEnd('%').Replace("$", string.Empty).Trim();
            var ok = celda.EsNumero
                ? decimal.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor)
                : decimal.TryParse(texto, NumberStyles.Number, Argentina, out valor);
            if (ok) return decimal.Round(valor, 6);
            Errores.Add($"{nombre}: '{celda.Texto.Trim()}' no es un número.");
            return null;
        }

        public bool? SiNo(Columna columna, string nombre)
        {
            if (Celda(columna) is not { } celda) return null;
            switch (Normalizar(celda.Texto))
            {
                case "si" or "s" or "1" or "true" or "verdadero" or "x" or "activo":
                    return true;
                case "no" or "n" or "0" or "false" or "falso" or "baja" or "inactivo":
                    return false;
                default:
                    Errores.Add($"{nombre}: '{celda.Texto.Trim()}' no es Sí o No.");
                    return null;
            }
        }
    }
}
