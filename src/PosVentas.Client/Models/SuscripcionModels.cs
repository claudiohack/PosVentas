using System.ComponentModel.DataAnnotations;

namespace PosVentas.Client.Models;

public enum EstadoSuscripcion
{
    Prueba = 1,
    Activa = 2,
    EnGracia = 3,
    SoloLectura = 4
}

public enum OrigenPagoSuscripcion
{
    Manual = 1,
    MercadoPago = 2
}

/// <summary>Resumen que viaja con la sesión: alcanza para el aviso de la barra superior.</summary>
public sealed record SuscripcionResumenDto(
    EstadoSuscripcion Estado,
    bool EnPrueba,
    DateTime? VenceUtc,
    DateTime? FinGraciaUtc,
    int DiasRestantes,
    string? Plan,
    List<string> Modulos,
    bool SinRestricciones);

public sealed record ModuloPlanDto(string Codigo, string Nombre, string Descripcion);

public sealed record PlanDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    decimal PrecioMensual,
    int? MaxUsuarios,
    int? MaxProductos,
    List<string> Modulos,
    int Orden,
    bool EstaActivo,
    bool EsPredeterminado);

public sealed record PagoSuscripcionDto(
    Guid Id, DateTime FechaUtc, decimal Monto, int Meses, DateTime PeriodoHastaUtc, OrigenPagoSuscripcion Origen,
    string Plan, string? Referencia, string? Nota);

public sealed record MiSuscripcionDto(
    SuscripcionResumenDto Resumen,
    PlanDto Plan,
    int UsuariosActivos,
    int ProductosActivos,
    List<PlanDto> PlanesDisponibles,
    List<PagoSuscripcionDto> Pagos,
    List<ModuloPlanDto> Modulos);

public sealed record ComercioSuscripcionDto(
    Guid ComercioId,
    string Nombre,
    string Slug,
    string? Cuit,
    DateTime FechaAltaUtc,
    bool EstaActivo,
    Guid PlanId,
    string Plan,
    EstadoSuscripcion Estado,
    bool EnPrueba,
    DateTime VenceUtc,
    int DiasRestantes,
    DateTime? UltimoPagoUtc);

/// <summary>Formulario de alta / edición de un plan (administración de la plataforma).</summary>
public sealed class PlanFormulario
{
    [Required(ErrorMessage = "Ingresá el código.")]
    [RegularExpression("^[A-Za-z0-9_]{3,30}$", ErrorMessage = "3 a 30 caracteres: letras, números y guiones bajos.")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ingresá el nombre.")]
    [StringLength(60)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Descripcion { get; set; }

    [Range(0, 100_000_000, ErrorMessage = "Ingresá un precio válido.")]
    public decimal PrecioMensual { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "Al menos 1 (vacío = sin límite).")]
    public int? MaxUsuarios { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "Al menos 1 (vacío = sin límite).")]
    public int? MaxProductos { get; set; }

    [Range(0, 1000)]
    public int Orden { get; set; }

    public HashSet<string> Modulos { get; set; } = [];
    public bool EstaActivo { get; set; } = true;
    public bool EsPredeterminado { get; set; }

    public static PlanFormulario Desde(PlanDto p) => new()
    {
        Codigo = p.Codigo,
        Nombre = p.Nombre,
        Descripcion = p.Descripcion,
        PrecioMensual = p.PrecioMensual,
        MaxUsuarios = p.MaxUsuarios,
        MaxProductos = p.MaxProductos,
        Orden = p.Orden,
        Modulos = [.. p.Modulos],
        EstaActivo = p.EstaActivo,
        EsPredeterminado = p.EsPredeterminado
    };
}

public static class TextosSuscripcion
{
    public static string Estado(EstadoSuscripcion e) => e switch
    {
        EstadoSuscripcion.Prueba => "Prueba gratuita",
        EstadoSuscripcion.Activa => "Activa",
        EstadoSuscripcion.EnGracia => "Vencida (en gracia)",
        _ => "Solo lectura"
    };

    public static string Clase(EstadoSuscripcion e) => e switch
    {
        EstadoSuscripcion.Prueba => "susc-prueba",
        EstadoSuscripcion.Activa => "susc-activa",
        EstadoSuscripcion.EnGracia => "susc-gracia",
        _ => "susc-bloqueada"
    };

    public static string Limite(int? maximo) => maximo is { } m ? m.ToString("N0") : "Sin límite";

    public static string Dias(int dias) => dias == 1 ? "1 día" : $"{dias} días";

    public static string Pesos(decimal valor) => valor.ToString("$ #,##0.00");
}
