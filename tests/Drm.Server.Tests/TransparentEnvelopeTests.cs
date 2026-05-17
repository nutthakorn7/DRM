using System.Text;
using FluentAssertions;
using Drm.Crypto;

namespace Drm.Server.Tests;

public sealed class TransparentEnvelopeTests
{
    private static readonly byte[] HmacSecret = Encoding.UTF8.GetBytes("test-secret-123");

    [Fact]
    public void Append_then_read_round_trips_metadata_and_preserves_prefix()
    {
        var original = "Hello, world! This is the original file content."u8.ToArray();
        var metadata = new TransparentMetadata(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "text/plain",
            "hello.txt",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            null);

        var stamped = TransparentEnvelope.AppendTrailer(original, metadata, HmacSecret);
        stamped.Length.Should().BeGreaterThan(original.Length);
        stamped.AsSpan(0, original.Length).ToArray().Should().Equal(original);

        var ok = TransparentEnvelope.TryReadTrailer(stamped, HmacSecret, out var readBack, out var originalLength);
        ok.Should().BeTrue();
        readBack.Should().BeEquivalentTo(metadata);
        originalLength.Should().Be(original.Length);
    }

    [Fact]
    public void TryReadTrailer_returns_false_when_no_trailer_present()
    {
        var bytes = "no drm trailer here"u8.ToArray();
        var ok = TransparentEnvelope.TryReadTrailer(bytes, HmacSecret, out var metadata, out var len);
        ok.Should().BeFalse();
        metadata.Should().BeNull();
        len.Should().Be(bytes.Length);
    }

    [Fact]
    public void TryReadTrailer_rejects_wrong_hmac_secret()
    {
        var original = "payload"u8.ToArray();
        var metadata = new TransparentMetadata(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "application/octet-stream", "file.bin", DateTimeOffset.UtcNow, null);
        var stamped = TransparentEnvelope.AppendTrailer(original, metadata, HmacSecret);

        var wrongSecret = Encoding.UTF8.GetBytes("different-secret");
        var ok = TransparentEnvelope.TryReadTrailer(stamped, wrongSecret, out var read, out _);
        ok.Should().BeFalse();
        read.Should().BeNull();
    }

    [Fact]
    public void TryReadTrailer_rejects_tampered_trailer()
    {
        var original = "payload"u8.ToArray();
        var metadata = new TransparentMetadata(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "application/octet-stream", "file.bin", DateTimeOffset.UtcNow, null);
        var stamped = TransparentEnvelope.AppendTrailer(original, metadata, HmacSecret);

        // Flip a byte in the middle of the trailer.
        stamped[original.Length + 12] ^= 0x55;

        var ok = TransparentEnvelope.TryReadTrailer(stamped, HmacSecret, out var read, out _);
        ok.Should().BeFalse();
        read.Should().BeNull();
    }

    [Fact]
    public void Strip_returns_original_bytes_when_trailer_is_valid()
    {
        var original = Encoding.UTF8.GetBytes("important data");
        var metadata = new TransparentMetadata(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "text/plain", "data.txt", DateTimeOffset.UtcNow, null);
        var stamped = TransparentEnvelope.AppendTrailer(original, metadata, HmacSecret);

        var stripped = TransparentEnvelope.StripTrailer(stamped, HmacSecret);
        stripped.Should().Equal(original);
    }
}
