using System.Text.RegularExpressions;
using PosVentas.Domain.Common;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Fiscal;

namespace PosVentas.Domain.Entities;

/// <summary>
/// Comercio cliente del SaaS. Es el tenant: su Id es el TenantId del resto de las entidades.
/// También guarda su configuración fiscal para facturar electrónicamente con ARCA (WSFEv1).
/// </summary>
public sealed partial class Comercio : Entity<Guid>
{
    public const int LongitudMaximaNombre = 150;
    public const int LongitudMaximaSlug = 50;
    public const int LongitudMaximaDomicilio = 200;
    public const int LongitudMaximaIngresosBrutos = 30;

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Identificador público y único del comercio (se usa en el login). Ej: "almacen-don-jose".</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Cuit { get; private set; }
    public bool EstaActivo { get; private set; }

    // ---------- Configuración fiscal ----------

    /// <summary>Condición frente al IVA del emisor: solo Responsable Inscripto o Monotributista pueden facturar.</summary>
    public CondicionIva? CondicionIva { get; private set; }

    /// <summary>Punto de venta habilitado en ARCA para "Factura electrónica - Web Services".</summary>
    public int? PuntoVentaAfip { get; private set; }

    public string? DomicilioFiscal { get; private set; }
    public string? IngresosBrutos { get; private set; }
    public DateOnly? InicioActividades { get; private set; }

    /// <summary>false = homologación (pruebas), true = producción.</summary>
    public bool AfipProduccion { get; private set; }

    /// <summary>Certificado + clave privada (PKCS#12) cifrados con Data Protection. Nunca se expone por la API.</summary>
    public string? CertificadoAfipProtegido { get; private set; }

    public string? CertificadoAfipSujeto { get; private set; }
    public DateTime? CertificadoAfipVenceUtc { get; private set; }

    /// <summary>Ticket de acceso de WSAA (token + sign) cifrado; vale ~12 h y se reutiliza hasta vencer.</summary>
    public string? TicketAccesoAfipProtegido { get; private set; }

    public DateTime? TicketAccesoAfipVenceUtc { get; private set; }

    public bool FacturacionConfigurada =>
        Cuit is not null && CondicionIva is not null && PuntoVentaAfip is > 0 && CertificadoAfipProtegido is not null;

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
            string.IsNullOrWhiteSpace(cuit) ? null : CuitArgentino.Normalizar(cuit));
    }

    public static string NormalizarSlug(string? slug)
    {
        var valor = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!SlugRegex().IsMatch(valor))
            throw new DomainException("El identificador del comercio debe tener entre 3 y 50 caracteres: letras minúsculas, números y guiones.");
        return valor;
    }

    public void ConfigurarDatosFiscales(
        string cuit, CondicionIva condicionIva, int puntoVenta, string? domicilioFiscal,
        string? ingresosBrutos, DateOnly? inicioActividades, bool produccion)
    {
        if (!CuitArgentino.EsValido(cuit))
            throw new DomainException("El CUIT del comercio no es válido.");
        if (condicionIva is not (Enums.CondicionIva.ResponsableInscripto or Enums.CondicionIva.Monotributista))
            throw new DomainException("Solo un Responsable Inscripto o un Monotributista pueden emitir facturas electrónicas.");
        if (puntoVenta is < 1 or > 99_998)
            throw new DomainException("El punto de venta debe estar entre 1 y 99998.");

        var cuitNormalizado = CuitArgentino.Normalizar(cuit);

        // El ticket de acceso depende del CUIT y del ambiente: si cambian, hay que pedir uno nuevo.
        if (cuitNormalizado != Cuit || produccion != AfipProduccion)
            OlvidarTicketAcceso();

        Cuit = cuitNormalizado;
        CondicionIva = condicionIva;
        PuntoVentaAfip = puntoVenta;
        DomicilioFiscal = Limpiar(domicilioFiscal);
        IngresosBrutos = Limpiar(ingresosBrutos);
        InicioActividades = inicioActividades;
        AfipProduccion = produccion;
        MarcarModificado();
    }

    public void ConfigurarCertificadoAfip(string certificadoProtegido, string sujeto, DateTime venceUtc)
    {
        if (string.IsNullOrWhiteSpace(certificadoProtegido))
            throw new DomainException("El certificado es obligatorio.");
        if (venceUtc <= DateTime.UtcNow)
            throw new DomainException("El certificado está vencido.");

        CertificadoAfipProtegido = certificadoProtegido;
        CertificadoAfipSujeto = sujeto;
        CertificadoAfipVenceUtc = venceUtc;
        OlvidarTicketAcceso();
        MarcarModificado();
    }

    public void GuardarTicketAcceso(string ticketProtegido, DateTime venceUtc)
    {
        TicketAccesoAfipProtegido = ticketProtegido;
        TicketAccesoAfipVenceUtc = venceUtc;
        MarcarModificado();
    }

    public void OlvidarTicketAcceso()
    {
        TicketAccesoAfipProtegido = null;
        TicketAccesoAfipVenceUtc = null;
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

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,48}[a-z0-9]$")]
    private static partial Regex SlugRegex();
}
