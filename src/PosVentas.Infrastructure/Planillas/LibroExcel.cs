using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using PosVentas.Application.Common.Exceptions;

namespace PosVentas.Infrastructure.Planillas;

/// <summary>Una hoja a escribir: encabezados, filas (string, decimal, int o bool) y ancho de columnas.</summary>
public sealed record HojaExcel(string Nombre, IReadOnlyList<string> Encabezados, IEnumerable<object?[]> Filas, IReadOnlyList<double>? Anchos = null);

/// <summary>Celda leída: el texto tal como está y si Excel la guardó como número (texto en formato invariante).</summary>
public sealed record CeldaExcel(string Texto, bool EsNumero);

/// <summary>
/// Lectura y escritura mínima de .xlsx con el Open XML SDK (MIT), sin dependencias de Office.
/// Los textos se escriben como cadenas en línea: un código de barras queda como texto y no pierde dígitos.
/// </summary>
public static class LibroExcel
{
    private const uint EstiloEncabezado = 1;
    private const uint EstiloImporte = 2;
    private const uint EstiloCantidad = 3;

    public static byte[] Escribir(params HojaExcel[] hojas)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var libro = doc.AddWorkbookPart();
            libro.Workbook = new Workbook();
            libro.AddNewPart<WorkbookStylesPart>().Stylesheet = CrearEstilos();
            var sheets = libro.Workbook.AppendChild(new Sheets());

            uint idHoja = 1;
            foreach (var hoja in hojas)
            {
                var parte = libro.AddNewPart<WorksheetPart>();
                parte.Worksheet = CrearHoja(hoja);
                sheets.Append(new Sheet { Id = libro.GetIdOfPart(parte), SheetId = idHoja++, Name = hoja.Nombre });
            }
            libro.Workbook.Save();
        }
        return ms.ToArray();
    }

    /// <summary>Filas no vacías de la primera hoja, con su número de fila de Excel (1 = primera).</summary>
    public static IReadOnlyList<(int Fila, CeldaExcel?[] Celdas)> LeerPrimeraHoja(Stream archivo)
    {
        try
        {
            using var doc = SpreadsheetDocument.Open(archivo, false);
            var libro = doc.WorkbookPart ?? throw Invalido();
            var hoja = libro.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw Invalido();
            if (hoja.Id?.Value is not { } id || libro.GetPartById(id) is not WorksheetPart { Worksheet: { } worksheet })
                throw Invalido();

            var compartidos = libro.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToArray() ?? [];
            var filas = new List<(int, CeldaExcel?[])>();
            foreach (var row in worksheet.Descendants<Row>())
            {
                var numeroFila = (int)(row.RowIndex?.Value ?? (uint)(filas.Count + 1));
                var celdas = new List<CeldaExcel?>();
                var siguiente = 0;
                foreach (var cell in row.Elements<Cell>())
                {
                    var columna = cell.CellReference?.Value is { } referencia ? IndiceColumna(referencia) : siguiente;
                    siguiente = columna + 1;
                    while (celdas.Count <= columna) celdas.Add(null);
                    celdas[columna] = Leer(cell, compartidos);
                }
                if (celdas.Any(c => !string.IsNullOrWhiteSpace(c?.Texto)))
                    filas.Add((numeroFila, celdas.ToArray()));
            }
            return filas;
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException or IOException
                                       or System.Xml.XmlException or ArgumentException)
        {
            throw Invalido();
        }
    }

    private static PlanillaInvalidaException Invalido() =>
        new("El archivo no es una planilla de Excel (.xlsx) válida. Guardala como \"Libro de Excel (.xlsx)\" y volvé a intentar.");

    private static CeldaExcel? Leer(Cell cell, string[] compartidos)
    {
        var valor = cell.CellValue?.Text;
        var tipo = cell.DataType?.Value;
        if (tipo == CellValues.SharedString)
            return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < compartidos.Length
                ? new CeldaExcel(compartidos[i], false) : null;
        if (tipo == CellValues.InlineString)
            return new CeldaExcel(cell.InlineString?.InnerText ?? string.Empty, false);
        if (tipo == CellValues.Boolean)
            return new CeldaExcel(valor == "1" ? "SI" : "NO", false);
        if (valor is null)
            return null;
        return tipo == CellValues.String || tipo == CellValues.Error
            ? new CeldaExcel(valor, false)
            : new CeldaExcel(valor, true); // número (o fecha serial) en formato invariante
    }

    private static int IndiceColumna(string referencia)
    {
        var indice = 0;
        foreach (var c in referencia)
        {
            if (!char.IsAsciiLetter(c)) break;
            indice = indice * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return Math.Max(0, indice - 1);
    }

    private static string NombreColumna(int indice)
    {
        var nombre = string.Empty;
        for (var n = indice + 1; n > 0; n = (n - 1) / 26)
            nombre = (char)('A' + (n - 1) % 26) + nombre;
        return nombre;
    }

    private static Worksheet CrearHoja(HojaExcel hoja)
    {
        var worksheet = new Worksheet();

        // Encabezado fijo al desplazarse
        worksheet.Append(new SheetViews(new SheetView(
            new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
            new Selection { Pane = PaneValues.BottomLeft })
        { WorkbookViewId = 0 }));

        if (hoja.Anchos is { Count: > 0 } anchos)
        {
            var columnas = new Columns();
            for (var i = 0; i < anchos.Count; i++)
                columnas.Append(new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = anchos[i], CustomWidth = true });
            worksheet.Append(columnas);
        }

        var datos = new SheetData();
        datos.Append(CrearFila(1, hoja.Encabezados.Cast<object?>().ToArray(), EstiloEncabezado));
        uint numero = 2;
        foreach (var fila in hoja.Filas)
            datos.Append(CrearFila(numero++, fila, null));
        worksheet.Append(datos);
        return worksheet;
    }

    private static Row CrearFila(uint numero, object?[] valores, uint? estilo)
    {
        var row = new Row { RowIndex = numero };
        for (var i = 0; i < valores.Length; i++)
        {
            var referencia = NombreColumna(i) + numero;
            Cell? cell = valores[i] switch
            {
                null => null,
                string s => new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(s) { Space = SpaceProcessingModeValues.Preserve }) },
                bool b => new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(b ? "Sí" : "No")) },
                int n => new Cell { DataType = CellValues.Number, CellValue = new CellValue(n.ToString(CultureInfo.InvariantCulture)) },
                CantidadExcel q => new Cell { DataType = CellValues.Number, CellValue = new CellValue(q.Valor.ToString(CultureInfo.InvariantCulture)), StyleIndex = EstiloCantidad },
                decimal d => new Cell { DataType = CellValues.Number, CellValue = new CellValue(d.ToString(CultureInfo.InvariantCulture)), StyleIndex = EstiloImporte },
                var otro => new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(Convert.ToString(otro, CultureInfo.InvariantCulture) ?? string.Empty)) }
            };
            if (cell is null) continue;
            cell.CellReference = referencia;
            if (estilo is { } e) cell.StyleIndex = e;
            row.Append(cell);
        }
        return row;
    }

    private static Stylesheet CrearEstilos() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.###" }) { Count = 1 },
        new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new Color { Rgb = "FFFFFFFF" }, new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 2 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF2456D3" }) { PatternType = PatternValues.Solid })) { Count = 3 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true },
            new CellFormat { NumberFormatId = 4, ApplyNumberFormat = true },   // #,##0.00
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }) { Count = 4 });
}

/// <summary>Cantidad (stock) que se escribe con hasta 3 decimales en lugar del formato de importe.</summary>
public readonly record struct CantidadExcel(decimal Valor);
