using System.Globalization;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Drm.Viewer.Windows;

public sealed record PrintWatermarkOptions(
    string Text,
    int OpacityPercent,
    string Position);

public static class PrintWatermarkComposer
{
    public static byte[] Stamp(byte[] originalPdfBytes, PrintWatermarkOptions options)
    {
        if (originalPdfBytes is null || originalPdfBytes.Length == 0)
        {
            throw new ArgumentException("PDF bytes required.", nameof(originalPdfBytes));
        }

        if (string.IsNullOrWhiteSpace(options.Text))
        {
            return originalPdfBytes;
        }

        using var input = new MemoryStream(originalPdfBytes);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        var clampedOpacity = Math.Clamp(options.OpacityPercent, 5, 100);
        var alpha = (byte)Math.Round(clampedOpacity * 2.55);
        var color = XColor.FromArgb(alpha, 80, 80, 80);

        foreach (var page in document.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);
            var width = page.Width.Point;
            var height = page.Height.Point;

            switch (options.Position?.ToLowerInvariant())
            {
                case "top":
                    DrawHeaderFooter(gfx, options.Text, color, width, y: 24);
                    break;
                case "bottom":
                    DrawHeaderFooter(gfx, options.Text, color, width, y: height - 24);
                    break;
                case "all-pages":
                    DrawHeaderFooter(gfx, options.Text, color, width, y: 24);
                    DrawHeaderFooter(gfx, options.Text, color, width, y: height - 24);
                    DrawDiagonal(gfx, options.Text, color, width, height);
                    break;
                case "diagonal":
                default:
                    DrawDiagonal(gfx, options.Text, color, width, height);
                    break;
            }
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void DrawDiagonal(XGraphics gfx, string text, XColor color, double width, double height)
    {
        var font = new XFont("Helvetica", 36, XFontStyleEx.Bold);
        var size = gfx.MeasureString(text, font);
        var centerX = width / 2.0;
        var centerY = height / 2.0;
        gfx.TranslateTransform(centerX, centerY);
        gfx.RotateTransform(-28);
        gfx.DrawString(
            text,
            font,
            new XSolidBrush(color),
            new XRect(-size.Width / 2.0, -size.Height / 2.0, size.Width, size.Height),
            XStringFormats.Center);
        gfx.RotateTransform(28);
        gfx.TranslateTransform(-centerX, -centerY);
    }

    private static void DrawHeaderFooter(XGraphics gfx, string text, XColor color, double width, double y)
    {
        var font = new XFont("Helvetica", 10, XFontStyleEx.Regular);
        gfx.DrawString(
            text,
            font,
            new XSolidBrush(color),
            new XRect(0, y - 6, width, 12),
            XStringFormats.Center);
    }

    public static string ResolveTokens(string pattern, Guid? userId, Guid? fileId, DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrEmpty(pattern)) return string.Empty;
        var now = (utcNow ?? DateTimeOffset.UtcNow);
        return pattern
            .Replace("{user}", userId?.ToString("N") ?? "anonymous", StringComparison.Ordinal)
            .Replace("{userId}", userId?.ToString("N") ?? "anonymous", StringComparison.Ordinal)
            .Replace("{file}", fileId?.ToString("N") ?? "", StringComparison.Ordinal)
            .Replace("{fileId}", fileId?.ToString("N") ?? "", StringComparison.Ordinal)
            .Replace("{time}", now.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
