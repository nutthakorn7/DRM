using System.Buffers.Binary;
using System.Text.Json;
using Drm.Crypto;
using Drm.Domain;

namespace Drm.Container;

public static class ProtectedFileWriter
{
    private const int Version = 1;
    private static readonly byte[] Magic = "DRM1"u8.ToArray();

    public static void Write(
        Stream stream,
        TenantId tenantId,
        ProtectedFileId fileId,
        string contentType,
        byte[] fileKey,
        byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(payload);

        var header = new ProtectedFileHeader(
            Version,
            tenantId.Value,
            fileId.Value,
            contentType,
            DateTimeOffset.UtcNow);

        var encrypted = EnvelopeCrypto.Encrypt(payload, fileKey, ProtectedFileAssociatedData.For(header));
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);

        stream.Write(Magic);
        WriteLengthPrefixed(stream, headerBytes);
        WriteLengthPrefixed(stream, encrypted.Nonce);
        WriteLengthPrefixed(stream, encrypted.Tag);
        WriteLengthPrefixed(stream, encrypted.Ciphertext);
    }

    private static void WriteLengthPrefixed(Stream stream, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        stream.Write(length);
        stream.Write(value);
    }
}
