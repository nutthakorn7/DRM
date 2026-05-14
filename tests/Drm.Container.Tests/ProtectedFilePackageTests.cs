using Drm.Container;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;
using System.Buffers.Binary;
using System.Text.Json;

namespace Drm.Container.Tests;

public sealed class ProtectedFilePackageTests
{
    [Fact]
    public void Write_then_read_round_trips_header_and_payload()
    {
        var tenantId = TenantId.New();
        var fileId = ProtectedFileId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var pdfBytes = "%PDF-1.7 test"u8.ToArray();

        using var stream = new MemoryStream();
        ProtectedFileWriter.Write(stream, tenantId, fileId, "application/pdf", fileKey, pdfBytes);
        stream.Position = 0;

        var package = ProtectedFileReader.Read(stream);
        var decrypted = package.Decrypt(fileKey);

        package.Header.TenantId.Should().Be(tenantId.Value);
        package.Header.FileId.Should().Be(fileId.Value);
        package.Header.ContentType.Should().Be("application/pdf");
        decrypted.Should().Equal(pdfBytes);
    }

    [Fact]
    public void Reader_rejects_non_drm_file()
    {
        using var stream = new MemoryStream("not drm"u8.ToArray());

        var action = () => ProtectedFileReader.Read(stream);

        action.Should().Throw<InvalidDataException>().WithMessage("Protected file magic header is invalid.");
    }

    [Fact]
    public void Read_rejects_oversized_header_length()
    {
        using var stream = new MemoryStream();
        stream.Write("DRM1"u8);
        WriteInt32BigEndian(stream, 64 * 1024 + 1);
        stream.Position = 0;

        var action = () => ProtectedFileReader.Read(stream);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*header length*");
    }

    [Theory]
    [InlineData(11, 16, "nonce length")]
    [InlineData(12, 15, "tag length")]
    public void Read_rejects_invalid_nonce_or_tag_length(int nonceLength, int tagLength, string expectedMessage)
    {
        var header = new ProtectedFileHeader(
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "application/pdf",
            DateTimeOffset.UtcNow);

        using var stream = new MemoryStream();
        stream.Write("DRM1"u8);
        WriteLengthPrefixed(stream, JsonSerializer.SerializeToUtf8Bytes(header));
        WriteLengthPrefixed(stream, new byte[nonceLength]);
        WriteLengthPrefixed(stream, new byte[tagLength]);
        WriteLengthPrefixed(stream, Array.Empty<byte>());
        stream.Position = 0;

        var action = () => ProtectedFileReader.Read(stream);

        action.Should().Throw<InvalidDataException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void Decrypt_rejects_tampered_created_at_header()
    {
        var tenantId = TenantId.New();
        var fileId = ProtectedFileId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();

        using var stream = new MemoryStream();
        ProtectedFileWriter.Write(stream, tenantId, fileId, "application/pdf", fileKey, "%PDF-1.7 test"u8.ToArray());
        stream.Position = 0;

        using var tamperedStream = TamperHeader(stream, header => header with
        {
            CreatedAtUtc = header.CreatedAtUtc.AddSeconds(1)
        });

        var package = ProtectedFileReader.Read(tamperedStream);
        var action = () => package.Decrypt(fileKey);

        action.Should().Throw<System.Security.Cryptography.AuthenticationTagMismatchException>();
    }

    private static MemoryStream TamperHeader(Stream protectedFile, Func<ProtectedFileHeader, ProtectedFileHeader> tamper)
    {
        using var source = new MemoryStream();
        protectedFile.CopyTo(source);
        var bytes = source.ToArray();

        var headerLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4, sizeof(int)));
        var header = JsonSerializer.Deserialize<ProtectedFileHeader>(bytes.AsSpan(8, headerLength))
            ?? throw new InvalidDataException("Header could not be deserialized in test.");
        var tamperedHeader = JsonSerializer.SerializeToUtf8Bytes(tamper(header));

        var output = new MemoryStream();
        output.Write(bytes.AsSpan(0, 4));
        WriteLengthPrefixed(output, tamperedHeader);
        output.Write(bytes.AsSpan(8 + headerLength));
        output.Position = 0;
        return output;
    }

    private static void WriteLengthPrefixed(Stream stream, byte[] value)
    {
        WriteInt32BigEndian(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
