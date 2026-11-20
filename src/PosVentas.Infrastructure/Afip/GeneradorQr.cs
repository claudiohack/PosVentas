using PosVentas.Application.Common.Interfaces;
using QRCoder;

namespace PosVentas.Infrastructure.Afip;

/// <summary>QR en PNG con QRCoder (no depende de System.Drawing, funciona en Windows y Linux).</summary>
public sealed class GeneradorQr : IGeneradorQr
{
    public string GenerarPngBase64(string contenido)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.M);
        return Convert.ToBase64String(new PngByteQRCode(datos).GetGraphic(pixelsPerModule: 5));
    }
}
