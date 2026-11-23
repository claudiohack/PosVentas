using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Application.Common.Security;
using PosVentas.Domain.Common;
using PosVentas.Domain.Entities;
using PosVentas.Domain.Enums;
using PosVentas.Domain.Fiscal;
using PosVentas.Domain.Seguridad;

namespace PosVentas.Application.Features.Facturacion;

/// <summary>Configuración fiscal visible. Nunca incluye el certificado, la clave privada ni el ticket de acceso.</summary>
public sealed record ConfiguracionFiscalDto(
    string RazonSocial,
    string? Cuit,
    CondicionIva? CondicionIva,
    int? PuntoVenta,
    string? DomicilioFiscal,
    string? IngresosBrutos,
    DateOnly? InicioActividades,
    bool Produccion,
    bool TieneCertificado,
    string? CertificadoSujeto,
    DateTime? CertificadoVenceUtc,
    bool FacturacionConfigurada)
{
    internal static ConfiguracionFiscalDto Desde(Comercio c) => new(
        c.Nombre, c.Cuit, c.CondicionIva, c.PuntoVentaAfip, c.DomicilioFiscal, c.IngresosBrutos, c.InicioActividades,
        c.AfipProduccion, c.CertificadoAfipProtegido is not null, c.CertificadoAfipSujeto, c.CertificadoAfipVenceUtc,
        c.FacturacionConfigurada);
}

internal static class ComercioActual
{
    public static async Task<Comercio> ObtenerAsync(IApplicationDbContext db, ICurrentTenantService tenant, CancellationToken ct) =>
        await db.Comercios.SingleAsync(c => c.Id == tenant.TenantId, ct);
}

// ---------- Consulta ----------

[RequierePermiso(Permisos.FacturacionConfigurar)]
public sealed record ObtenerConfiguracionFiscalQuery : IRequest<ConfiguracionFiscalDto>;

public sealed class ObtenerConfiguracionFiscalQueryHandler(IApplicationDbContext db, ICurrentTenantService tenant)
    : IRequestHandler<ObtenerConfiguracionFiscalQuery, ConfiguracionFiscalDto>
{
    public async Task<ConfiguracionFiscalDto> Handle(ObtenerConfiguracionFiscalQuery request, CancellationToken ct) =>
        ConfiguracionFiscalDto.Desde(await ComercioActual.ObtenerAsync(db, tenant, ct));
}

// ---------- Datos fiscales ----------

[RequierePermiso(Permisos.FacturacionConfigurar)]
public sealed record ActualizarDatosFiscalesCommand(
    string Cuit,
    CondicionIva CondicionIva,
    int PuntoVenta,
    string? DomicilioFiscal,
    string? IngresosBrutos,
    DateOnly? InicioActividades,
    bool Produccion) : IRequest<ConfiguracionFiscalDto>;

public sealed class ActualizarDatosFiscalesCommandValidator : AbstractValidator<ActualizarDatosFiscalesCommand>
{
    public ActualizarDatosFiscalesCommandValidator()
    {
        RuleFor(x => x.Cuit).NotEmpty().Must(CuitArgentino.EsValido).WithMessage("El CUIT no es válido (revisá el dígito verificador).");
        RuleFor(x => x.CondicionIva).Must(c => c is CondicionIva.ResponsableInscripto or CondicionIva.Monotributista)
            .WithMessage("Solo un Responsable Inscripto o un Monotributista pueden facturar electrónicamente.");
        RuleFor(x => x.PuntoVenta).InclusiveBetween(1, 99_998);
        RuleFor(x => x.DomicilioFiscal).MaximumLength(Comercio.LongitudMaximaDomicilio);
        RuleFor(x => x.IngresosBrutos).MaximumLength(Comercio.LongitudMaximaIngresosBrutos);
    }
}

public sealed class ActualizarDatosFiscalesCommandHandler(IApplicationDbContext db, ICurrentTenantService tenant)
    : IRequestHandler<ActualizarDatosFiscalesCommand, ConfiguracionFiscalDto>
{
    public async Task<ConfiguracionFiscalDto> Handle(ActualizarDatosFiscalesCommand r, CancellationToken ct)
    {
        var comercio = await ComercioActual.ObtenerAsync(db, tenant, ct);
        comercio.ConfigurarDatosFiscales(r.Cuit, r.CondicionIva, r.PuntoVenta, r.DomicilioFiscal, r.IngresosBrutos,
            r.InicioActividades, r.Produccion);
        await db.SaveChangesAsync(ct);
        return ConfiguracionFiscalDto.Desde(comercio);
    }
}

// ---------- Certificado digital ----------

public enum FormatoCertificado
{
    /// <summary>Archivo .p12 / .pfx (certificado + clave privada), opcionalmente con contraseña.</summary>
    Pkcs12 = 1,

    /// <summary>Certificado .crt (PEM, el que descarga ARCA) + clave privada .key (PEM) con la que se generó el CSR.</summary>
    Pem = 2
}

[RequierePermiso(Permisos.FacturacionConfigurar)]
public sealed record CargarCertificadoAfipCommand(
    FormatoCertificado Formato,
    string? Pkcs12Base64,
    string? CertificadoPem,
    string? ClavePrivadaPem,
    string? Password) : IRequest<ConfiguracionFiscalDto>;

public sealed class CargarCertificadoAfipCommandValidator : AbstractValidator<CargarCertificadoAfipCommand>
{
    private const int LongitudMaxima = 64 * 1024;

    public CargarCertificadoAfipCommandValidator()
    {
        RuleFor(x => x.Formato).IsInEnum();
        RuleFor(x => x.Pkcs12Base64).NotEmpty().MaximumLength(LongitudMaxima).When(x => x.Formato == FormatoCertificado.Pkcs12);
        RuleFor(x => x.CertificadoPem).NotEmpty().MaximumLength(LongitudMaxima).When(x => x.Formato == FormatoCertificado.Pem);
        RuleFor(x => x.ClavePrivadaPem).NotEmpty().MaximumLength(LongitudMaxima).When(x => x.Formato == FormatoCertificado.Pem);
        RuleFor(x => x.Password).MaximumLength(256);
    }
}

public sealed class CargarCertificadoAfipCommandHandler(
    IApplicationDbContext db, ICurrentTenantService tenant, ICertificadoAfipProtector protector)
    : IRequestHandler<CargarCertificadoAfipCommand, ConfiguracionFiscalDto>
{
    public async Task<ConfiguracionFiscalDto> Handle(CargarCertificadoAfipCommand r, CancellationToken ct)
    {
        var comercio = await ComercioActual.ObtenerAsync(db, tenant, ct);

        CertificadoAfipProcesado certificado;
        if (r.Formato == FormatoCertificado.Pkcs12)
        {
            byte[] contenido;
            try
            {
                contenido = Convert.FromBase64String(r.Pkcs12Base64!);
            }
            catch (FormatException)
            {
                throw new DomainException("El archivo del certificado no tiene un formato válido.");
            }
            certificado = protector.ProcesarPkcs12(contenido, r.Password);
        }
        else
        {
            certificado = protector.ProcesarPem(r.CertificadoPem!, r.ClavePrivadaPem!, r.Password);
        }

        // Un certificado de ARCA identifica al contribuyente: no puede ser de otro CUIT.
        if (certificado.CuitDelCertificado is { } cuitCert && comercio.Cuit is { } cuitComercio && cuitCert != cuitComercio)
            throw new DomainException(
                $"El certificado pertenece al CUIT {CuitArgentino.Formatear(cuitCert)}, no al del comercio ({CuitArgentino.Formatear(cuitComercio)}).");

        comercio.ConfigurarCertificadoAfip(certificado.Protegido, certificado.Sujeto, certificado.VenceUtc);
        await db.SaveChangesAsync(ct);
        return ConfiguracionFiscalDto.Desde(comercio);
    }
}

// ---------- Verificación de conexión ----------

[RequierePermiso(Permisos.FacturacionConfigurar)]
public sealed record VerificarConexionAfipCommand : IRequest<EstadoServicioAfip>;

public sealed class VerificarConexionAfipCommandHandler(IApplicationDbContext db, ICurrentTenantService tenant, IAfipService afip)
    : IRequestHandler<VerificarConexionAfipCommand, EstadoServicioAfip>
{
    public async Task<EstadoServicioAfip> Handle(VerificarConexionAfipCommand request, CancellationToken ct)
    {
        var comercio = await ComercioActual.ObtenerAsync(db, tenant, ct);
        if (!comercio.FacturacionConfigurada)
            throw new DomainException("Completá los datos fiscales y cargá el certificado antes de probar la conexión.");

        var tipo = comercio.CondicionIva == CondicionIva.Monotributista ? TipoComprobante.FacturaC : TipoComprobante.FacturaB;
        return await afip.VerificarConexionAsync(comercio.Id, tipo, ct);
    }
}
