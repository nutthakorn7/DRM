using Drm.Watermark;
using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Drm.Watermark.Tests;

[Collection("PdfSharpFonts")]
public sealed class PrintWatermarkComposerTests
{
    [Fact]
    public void Stamp_returns_original_bytes_when_text_is_empty()
    {
        var original = MakeOnePagePdf();

        var result = PrintWatermarkComposer.Stamp(
            original,
            new PrintWatermarkOptions(Text: "", OpacityPercent: 50, Position: "diagonal"));

        // No mutation means the byte-array reference is the same one we passed in.
        result.Should().BeSameAs(original);
    }

    [Fact]
    public void Stamp_returns_original_bytes_when_text_is_whitespace()
    {
        var original = MakeOnePagePdf();

        var result = PrintWatermarkComposer.Stamp(
            original,
            new PrintWatermarkOptions(Text: "   ", OpacityPercent: 50, Position: "diagonal"));

        result.Should().BeSameAs(original);
    }

    [Fact]
    public void Stamp_throws_when_pdf_bytes_are_null_or_empty()
    {
        var act = () => PrintWatermarkComposer.Stamp(
            Array.Empty<byte>(),
            new PrintWatermarkOptions("hi", 50, "diagonal"));
        act.Should().Throw<ArgumentException>().WithMessage("*PDF bytes required*");

        var actNull = () => PrintWatermarkComposer.Stamp(
            null!,
            new PrintWatermarkOptions("hi", 50, "diagonal"));
        actNull.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Stamp_diagonal_returns_modified_pdf()
    {
        var original = MakeOnePagePdf();

        var stamped = PrintWatermarkComposer.Stamp(
            original,
            new PrintWatermarkOptions("CONFIDENTIAL", 75, "diagonal"));

        stamped.Should().NotBeSameAs(original, "the stamp must produce a new byte array");
        stamped.Length.Should().BeGreaterThan(original.Length, "watermark content adds drawing instructions");

        // Sanity: still a valid PDF that PdfSharp can re-open with the same page count.
        using var stream = new MemoryStream(stamped);
        using var reopened = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Stamp_all_pages_position_modifies_every_page()
    {
        var original = MakeMultiPagePdf(pageCount: 3);

        var stamped = PrintWatermarkComposer.Stamp(
            original,
            new PrintWatermarkOptions("RESTRICTED", 50, "all-pages"));

        using var stream = new MemoryStream(stamped);
        using var reopened = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        reopened.PageCount.Should().Be(3);
        stamped.Length.Should().BeGreaterThan(original.Length);
    }

    [Theory]
    [InlineData("top")]
    [InlineData("bottom")]
    [InlineData("diagonal")]
    [InlineData("all-pages")]
    [InlineData("DIAGONAL")] // case-insensitive position
    [InlineData("unrecognized-position")] // falls back to diagonal, must not throw
    public void Stamp_accepts_every_documented_position_plus_fallback(string position)
    {
        var original = MakeOnePagePdf();

        var act = () => PrintWatermarkComposer.Stamp(
            original,
            new PrintWatermarkOptions("WATERMARK", 50, position));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(500)]
    public void Stamp_clamps_opacity_to_valid_range_without_throwing(int rawOpacity)
    {
        // Anything outside [5, 100] should still produce a valid stamped PDF.
        var original = MakeOnePagePdf();

        var act = () => PrintWatermarkComposer.Stamp(
            original,
            new PrintWatermarkOptions("X", rawOpacity, "diagonal"));

        act.Should().NotThrow();
    }

    // ----------------------------- ResolveTokens -----------------------------

    [Fact]
    public void ResolveTokens_replaces_all_documented_tokens()
    {
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var fileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var now = new DateTimeOffset(2026, 5, 21, 12, 30, 0, TimeSpan.Zero);

        var resolved = PrintWatermarkComposer.ResolveTokens(
            "user={user} userId={userId} file={file} fileId={fileId} at {time}",
            userId, fileId, now);

        resolved.Should().Be(
            "user=11111111222233334444555555555555 " +
            "userId=11111111222233334444555555555555 " +
            "file=aaaaaaaabbbbccccddddeeeeeeeeeeee " +
            "fileId=aaaaaaaabbbbccccddddeeeeeeeeeeee " +
            "at 2026-05-21 12:30:00 UTC");
    }

    [Fact]
    public void ResolveTokens_uses_anonymous_when_user_is_null()
    {
        var result = PrintWatermarkComposer.ResolveTokens(
            "viewer:{user}", userId: null, fileId: null,
            utcNow: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        result.Should().Be("viewer:anonymous");
    }

    [Fact]
    public void ResolveTokens_uses_empty_string_when_file_is_null()
    {
        var result = PrintWatermarkComposer.ResolveTokens(
            "[{file}]", userId: Guid.NewGuid(), fileId: null,
            utcNow: DateTimeOffset.UtcNow);

        result.Should().Be("[]");
    }

    [Fact]
    public void ResolveTokens_returns_empty_for_empty_pattern()
    {
        PrintWatermarkComposer.ResolveTokens("", Guid.NewGuid(), Guid.NewGuid()).Should().Be(string.Empty);
        PrintWatermarkComposer.ResolveTokens(null!, Guid.NewGuid(), Guid.NewGuid()).Should().Be(string.Empty);
    }

    [Fact]
    public void ResolveTokens_leaves_unknown_tokens_in_place()
    {
        // Unknown tokens are left as-is so a template author can see their
        // typo on the rendered watermark instead of getting silent blanks.
        var result = PrintWatermarkComposer.ResolveTokens(
            "{tenant} - {user}", Guid.Parse("11111111-2222-3333-4444-555555555555"), null);

        result.Should().Be("{tenant} - 11111111222233334444555555555555");
    }

    [Fact]
    public void ResolveTokens_handles_repeated_tokens()
    {
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var result = PrintWatermarkComposer.ResolveTokens(
            "{user} / {user} / {user}", userId, null);

        result.Should().Be(
            "11111111222233334444555555555555 / " +
            "11111111222233334444555555555555 / " +
            "11111111222233334444555555555555");
    }

    [Fact]
    public void ResolveTokens_uses_invariant_culture_for_time()
    {
        var thread = System.Threading.Thread.CurrentThread;
        var originalCulture = thread.CurrentCulture;
        try
        {
            thread.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            var result = PrintWatermarkComposer.ResolveTokens(
                "{time}", null, null,
                utcNow: new DateTimeOffset(2026, 5, 21, 9, 0, 0, TimeSpan.Zero));

            // Year must be 2026, not the Buddhist-calendar 2569 that th-TH would render.
            result.Should().StartWith("2026-");
        }
        finally
        {
            thread.CurrentCulture = originalCulture;
        }
    }

    // ----------------------------- helpers -----------------------------

    private static byte[] MakeOnePagePdf() => MakeMultiPagePdf(1);

    private static byte[] MakeMultiPagePdf(int pageCount)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pageCount; i += 1)
        {
            document.AddPage();
        }
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
