using PdfSharp.Fonts;

namespace Drm.Watermark.Tests;

/// <summary>
/// Registers a font resolver so PdfSharp can render text on Linux CI and
/// macOS dev machines (Windows would auto-resolve via system fonts).
///
/// Strategy: scan a small set of well-known font directories for a font we
/// can load. Maps every requested family name ("Helvetica", anything) to
/// whichever file we found. PdfSharp will measure and embed correctly — the
/// glyph metrics differ from real Helvetica, but the tests only assert that
/// SOMETHING was drawn (bytes grew, page count preserved), not exact glyph
/// positions. Tests that assert glyph metrics belong on a Windows test
/// runner with real Helvetica.
///
/// xUnit collection fixture so it runs once across all tests in the
/// collection, never reinstalled mid-run (the resolver is a global setting).
/// </summary>
public sealed class PdfSharpFontFixture
{
    private static int initialized;

    public PdfSharpFontFixture()
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1) return;

        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = new SystemFontResolver();
        }
    }
}

[CollectionDefinition("PdfSharpFonts")]
public sealed class PdfSharpFontCollection : ICollectionFixture<PdfSharpFontFixture> { }

internal sealed class SystemFontResolver : IFontResolver
{
    private static readonly string[] CandidateFontFiles =
    [
        // Linux (CI)
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        // macOS dev
        "/Library/Fonts/Arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/System/Library/Fonts/HelveticaNeue.ttc",
        // Windows (when running tests locally on a dev box)
        @"C:\Windows\Fonts\arial.ttf",
        @"C:\Windows\Fonts\arialbd.ttf",
    ];

    private readonly Lazy<string?> resolvedPath = new(() =>
        CandidateFontFiles.FirstOrDefault(File.Exists));

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Map every requested family to the same face we found.
        // Bold/italic style flags are ignored — good enough for tests
        // that only need ANY font to satisfy PdfSharp's measurement step.
        if (resolvedPath.Value is null) return null;
        var suffix = (isBold, isItalic) switch
        {
            (true, true)  => "BI",
            (true, false) => "B",
            (false, true) => "I",
            _             => "R"
        };
        return new FontResolverInfo($"resolved-{suffix}.ttf");
    }

    public byte[]? GetFont(string faceName)
    {
        if (resolvedPath.Value is null) return null;
        return File.ReadAllBytes(resolvedPath.Value);
    }
}
