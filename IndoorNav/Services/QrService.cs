using QRCoder;
using SkiaSharp;

namespace IndoorNav.Services;

/// <summary>
/// Generates QR-code images using QRCoder + SkiaSharp (no GDI+ dependency).
/// The QR content format: <c>indoornav://node/{nodeId}</c>  (deep-link URI).
/// Legacy format <c>indoornav-node:{nodeId}</c> is accepted when parsing.
/// </summary>
public class QrService
{
    // URI that Android/iOS can handle as a deep link: indoornav://node/{guid}
    private const string NodeUri = "indoornav://node/";

    // ── Public helpers ──────────────────────────────────────────────────────────

    /// <summary>Returns the deep-link URI for a given node ID.</summary>
    public string GetNodeQrContent(string nodeId) => NodeUri + nodeId;

    /// <summary>
    /// Parses QR content and returns the raw node ID, or <c>null</c> if unrecognised.
    /// Delegates to <see cref="DeepLinkService.ParseUri"/> for unified handling.
    /// </summary>
    public string? ParseNodeId(string? content) => DeepLinkService.ParseUri(content);

    // ── PNG generation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a QR PNG as a byte array using SkiaSharp for rendering (no GDI+).
    /// </summary>
    /// <param name="content">Text / URI to encode.</param>
    /// <param name="moduleSize">Pixels per QR module (default 10).</param>
    /// <param name="quietZone">Empty border in modules (default 4).</param>
    public byte[] GeneratePng(string content, int moduleSize = 10, int quietZone = 4)
    {
        using var gen    = new QRCodeGenerator();
        using var data   = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var matrix       = data.ModuleMatrix;
        int modules      = matrix.Count;
        int total        = (modules + 2 * quietZone) * moduleSize;

        using var bitmap = new SKBitmap(total, total);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var dark = new SKPaint { Color = SKColors.Black };
        for (int row = 0; row < modules; row++)
            for (int col = 0; col < modules; col++)
                if (matrix[row][col])
                {
                    float x = (col + quietZone) * moduleSize;
                    float y = (row + quietZone) * moduleSize;
                    canvas.DrawRect(x, y, moduleSize, moduleSize, dark);
                }

        using var img  = SKImage.FromBitmap(bitmap);
        using var enc  = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    /// <summary>Generates a QR code and wraps the PNG bytes as a MAUI <see cref="ImageSource"/>.</summary>
    public ImageSource GenerateImageSource(string content, int moduleSize = 10)
    {
        var bytes = GeneratePng(content, moduleSize);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    // ── QR decoding from image bytes ────────────────────────────────────────

    /// <summary>
    /// Decodes the first QR code found in <paramref name="imageBytes"/> (PNG/JPG/BMP).
    /// Returns the raw text content or <c>null</c> if no QR was found / an error occurred.
    /// </summary>
    public string? DecodeFromBytes(byte[] imageBytes)
    {
        try
        {
            using var bmpOrig = SKBitmap.Decode(imageBytes);
            if (bmpOrig == null) return null;

            // Приводим к RGBA8888, чтобы .Pixels всегда давал правильные каналы
            var bmp = bmpOrig.ColorType == SKColorType.Rgba8888
                ? bmpOrig
                : bmpOrig.Copy(SKColorType.Rgba8888);

            int w = bmp.Width, h = bmp.Height;
            var pix = bmp.Pixels;             // SKColor[] — R/G/B/A per pixel
            var rgb  = new byte[w * h * 3];
            for (int i = 0; i < pix.Length; i++)
            {
                rgb[i * 3]     = pix[i].Red;
                rgb[i * 3 + 1] = pix[i].Green;
                rgb[i * 3 + 2] = pix[i].Blue;
            }
            if (!ReferenceEquals(bmp, bmpOrig)) bmp.Dispose();

            // BitmapFormat.RGB24 обязателен — 3-параметровый ctor по умолчанию
            // передаёт BitmapFormat.Unknown, что ломает расчёт яркости в ZXing.Net 0.16.x
            var src   = new ZXing.RGBLuminanceSource(rgb, w, h,
                            ZXing.RGBLuminanceSource.BitmapFormat.RGB24);
            var bb    = new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(src));
            var hints = new Dictionary<ZXing.DecodeHintType, object>
            {
                [ZXing.DecodeHintType.TRY_HARDER] = true,
                [ZXing.DecodeHintType.POSSIBLE_FORMATS] =
                    new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE }
            };
            var result = new ZXing.MultiFormatReader().decode(bb, hints);
            return result?.Text;
        }
        catch
        {
            return null;
        }
    }
}
