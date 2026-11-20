using PosVentas.Domain.Enums;

namespace PosVentas.Domain.Fiscal;

/// <summary>Códigos y letras de comprobante según las tablas de ARCA (WSFEv1, FEParamGetTiposCbte).</summary>
public static class TiposComprobanteAfip
{
    public static int Codigo(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.FacturaA => 1,
        TipoComprobante.NotaCreditoA => 3,
        TipoComprobante.FacturaB => 6,
        TipoComprobante.NotaCreditoB => 8,
        TipoComprobante.FacturaC => 11,
        TipoComprobante.NotaCreditoC => 13,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "El ticket interno no tiene código ARCA.")
    };

    public static char Letra(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.FacturaA or TipoComprobante.NotaCreditoA => 'A',
        TipoComprobante.FacturaB or TipoComprobante.NotaCreditoB => 'B',
        TipoComprobante.FacturaC or TipoComprobante.NotaCreditoC => 'C',
        _ => 'X'
    };

    public static bool EsFactura(TipoComprobante tipo) =>
        tipo is TipoComprobante.FacturaA or TipoComprobante.FacturaB or TipoComprobante.FacturaC;

    public static bool EsNotaCredito(TipoComprobante tipo) =>
        tipo is TipoComprobante.NotaCreditoA or TipoComprobante.NotaCreditoB or TipoComprobante.NotaCreditoC;

    public static string Descripcion(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.Ticket => "Ticket interno",
        _ when EsNotaCredito(tipo) => $"Nota de crédito {Letra(tipo)}",
        _ => $"Factura {Letra(tipo)}"
    };
}

/// <summary>Tipos de documento del receptor (tabla de ARCA).</summary>
public static class TiposDocumentoAfip
{
    public const int Cuit = 80;
    public const int Dni = 96;
    public const int SinIdentificar = 99;
}

/// <summary>Condición frente al IVA del receptor (campo CondicionIVAReceptorId, RG 5616).</summary>
public static class CondicionIvaReceptorAfip
{
    public static int Codigo(CondicionIva condicion) => condicion switch
    {
        CondicionIva.ResponsableInscripto => 1,
        CondicionIva.Exento => 4,
        CondicionIva.ConsumidorFinal => 5,
        CondicionIva.Monotributista => 6,
        _ => 5
    };
}

/// <summary>Alícuotas de IVA (tabla de ARCA).</summary>
public static class AlicuotasIvaAfip
{
    public static int Codigo(decimal porcentaje) => porcentaje switch
    {
        0m => 3,
        2.5m => 9,
        5m => 8,
        10.5m => 4,
        21m => 5,
        27m => 6,
        _ => throw new Common.DomainException($"La alícuota de IVA {porcentaje}% no está habilitada por ARCA.")
    };
}

/// <summary>Fecha de emisión de los comprobantes: la fecha calendario en la Argentina (UTC-3, sin horario de verano).</summary>
public static class FechaFiscal
{
    private static readonly TimeZoneInfo ZonaArgentina = ObtenerZona();

    public static DateOnly Hoy() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZonaArgentina));

    public static DateTime ALocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaArgentina);

    /// <summary>Instante UTC en que empieza el día indicado en la Argentina (00:00 ART).</summary>
    public static DateTime InicioDelDiaUtc(DateOnly dia) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dia.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), ZonaArgentina);

    private static TimeZoneInfo ObtenerZona()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("ART", TimeSpan.FromHours(-3), "Argentina", "Argentina");
        }
    }
}

/// <summary>CUIT / CUIL argentino: 11 dígitos con dígito verificador módulo 11.</summary>
public static class CuitArgentino
{
    private static readonly int[] Pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    /// <summary>Deja solo los dígitos ("30-71234567-1" → "30712345671").</summary>
    public static string Normalizar(string? valor) => new((valor ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    public static bool EsValido(string? valor)
    {
        var cuit = Normalizar(valor);
        if (cuit.Length != 11)
            return false;

        var suma = 0;
        for (var i = 0; i < 10; i++)
            suma += (cuit[i] - '0') * Pesos[i];

        var verificador = 11 - suma % 11;
        verificador = verificador switch { 11 => 0, 10 => 9, _ => verificador };
        return cuit[10] - '0' == verificador;
    }

    public static string Formatear(string cuit) =>
        cuit.Length == 11 ? $"{cuit[..2]}-{cuit[2..10]}-{cuit[10..]}" : cuit;
}
