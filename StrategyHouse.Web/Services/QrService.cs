using QRCoder;

namespace StrategyHouse.Web.Services;

/// <summary>
/// Generates QR codes for in-room flows: baseline check (session start) and
/// end-of-session survey. Attendees scan with their phones; no iPad sharing.
/// </summary>
public class QrService
{
    public string GenerateBase64Png(string content, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
