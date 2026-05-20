using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Drm.Watermark;

/// <summary>
/// Options for a single watermark stamp pass.
/// </summary>
/// <param name="Text">The fully-resolved watermark text. Empty/whitespace skips stamping.</param>
/// <param name="OpacityPercent">5–100. Lower = more transparent. Clamped on use.</param>
/// <param name="Position">"diagonal" (default), "top", "bottom", or "all-pages".</param>
public sealed record PrintWatermarkOptions(
    string Text,
    int OpacityPercent,
    string Position);

/// <summary>
/// Stamps a watermark onto every page of a PDF before it is sent to print or
/// exported as a hard copy. Used by the WPF viewer's PrintButton and any
/// other surface that needs a watermarked deliverable.
///
/// Pure PdfSharp — no Windows API dependency — so this lives in a
/// cross-platform library and gets unit-test coverage on Linux CI. The
/// physical print invocation that consumes the stamped bytes remains in the
/// Windows viewer.
/// </summary>
public static class PrintWatermarkComposer
{
    /// <summary>
    /// Returns a new PDF byte stream with <paramref name="options"/>.Text
    /// stamped onto every page. If the text is empty, the original bytes
    /// are returned unmodified (no-op so callers can drop unconditionally
    /// into a print pipeline).
    /// </summary>
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

    /// <summary>
    /// Resolves <c>{user}</c>, <c>{userId}</c>, <c>{file}</c>, <c>{fileId}</c>,
    /// and <c>{time}</c> tokens in a watermark template. Unknown tokens are
    /// left as-is so a template author can spot a typo on the rendered output.
    /// </summary>
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
