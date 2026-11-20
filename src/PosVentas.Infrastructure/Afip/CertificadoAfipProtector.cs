using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using PosVentas.Application.Common.Interfaces;
using PosVentas.Domain.Common;

namespace PosVentas.Infrastructure.Afip;

/// <summary>
/// Valida el certificado de ARCA y lo cifra con ASP.NET Core Data Protection antes de persistirlo.
/// El anillo de claves de Data Protection debe configurarse fuera de la base (ver DataProtection:DirectorioClaves).
/// </summary>
public sealed partial class CertificadoAfipProtector(IDataProtectionProvider proveedor) : ICertificadoAfipProtector
{
    private const string PropositoCertificado = "PosVentas.Afip.Certificado.v1";
    private const string PropositoTicket = "PosVentas.Afip.TicketAcceso.v1";

    private const X509KeyStorageFlags Flags = X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;

    private IDataProtector Certificados => proveedor.CreateProtector(PropositoCertificado);
    private IDataProtector Tickets => proveedor.CreateProtector(PropositoTicket);

    public CertificadoAfipProcesado ProcesarPkcs12(byte[] contenido, string? password)
    {
        X509Certificate2 certificado;
        try
        {
            certificado = X509CertificateLoader.LoadPkcs12(contenido, password, Flags);
        }
        catch (CryptographicException)
        {
            throw new DomainException("No se pudo abrir el certificado: revisá el archivo .p12 y su contraseña.");
        }

        using (certificado)
            return Proteger(certificado);
    }

    public CertificadoAfipProcesado ProcesarPem(string certificadoPem, string clavePrivadaPem, string? password)
    {
        X509Certificate2 certificado;
        try
        {
            certificado = string.IsNullOrEmpty(password)
                ? X509Certificate2.CreateFromPem(certificadoPem, clavePrivadaPem)
                : X509Certificate2.CreateFromEncryptedPem(certificadoPem, clavePrivadaPem, password);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new DomainException("No se pudo leer el certificado: verificá que el .crt y la clave privada (.key) sean del mismo pedido.");
        }

        using (certificado)
            return Proteger(certificado);
    }

    /// <summary>Abre el certificado guardado para firmar el pedido de acceso a WSAA.</summary>
    public X509Certificate2 Abrir(string protegido) =>
        X509CertificateLoader.LoadPkcs12(Certificados.Unprotect(Convert.FromBase64String(protegido)), null, X509KeyStorageFlags.EphemeralKeySet);

    public string ProtegerTicket(string token, string sign) =>
        Convert.ToBase64String(Tickets.Protect(JsonSerializer.SerializeToUtf8Bytes(new TicketAcceso(token, sign))));

    public (string Token, string Sign) AbrirTicket(string protegido)
    {
        var ticket = JsonSerializer.Deserialize<TicketAcceso>(Tickets.Unprotect(Convert.FromBase64String(protegido)))!;
        return (ticket.Token, ticket.Sign);
    }

    private CertificadoAfipProcesado Proteger(X509Certificate2 certificado)
    {
        if (!certificado.HasPrivateKey)
            throw new DomainException("El certificado no incluye la clave privada.");
        if (certificado.GetRSAPrivateKey() is null)
            throw new DomainException("ARCA requiere un certificado con clave RSA.");

        var venceUtc = certificado.NotAfter.ToUniversalTime();
        if (venceUtc <= DateTime.UtcNow)
            throw new DomainException($"El certificado venció el {certificado.NotAfter:dd/MM/yyyy}.");

        // Se reexporta sin contraseña: la protección la da Data Protection, no una clave que habría que guardar.
        var pkcs12 = certificado.Export(X509ContentType.Pkcs12);
        var protegido = Convert.ToBase64String(Certificados.Protect(pkcs12));
        CryptographicOperations.ZeroMemory(pkcs12);

        var cuit = CuitEnSujeto().Match(certificado.Subject) is { Success: true } m ? m.Groups[1].Value : null;
        return new CertificadoAfipProcesado(protegido, certificado.Subject, venceUtc, cuit);
    }

    // Los certificados de ARCA llevan SERIALNUMBER=CUIT 20123456786 en el sujeto.
    [GeneratedRegex(@"SERIALNUMBER=CUIT\s*(\d{11})", RegexOptions.IgnoreCase)]
    private static partial Regex CuitEnSujeto();

    private sealed record TicketAcceso(string Token, string Sign);
}
