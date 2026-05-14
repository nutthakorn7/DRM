using System.Buffers.Binary;
using System.Text.Json;
using Drm.Crypto;

namespace Drm.Container;

public static class ProtectedFileReader
{
    private const int MaxHeaderLengthBytes = 64 * 1024;
    private const int MaxCiphertextLengthBytes = 512 * 1024 * 1024;
    private static readonly byte[] Magic = "DRM1"u8.ToArray();

    public static ProtectedFilePackage Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> magic = stackalloc byte[Magic.Length];
        ReadExactly(stream, magic);

        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException("Protected file magic header is invalid.");
        }

        var headerBytes = ReadLengthPrefixed(stream, "header", MaxHeaderLengthBytes);
        var header = JsonSerializer.Deserialize<ProtectedFileHeader>(headerBytes)
            ?? throw new InvalidDataException("Protected file header is invalid.");

        var nonce = ReadLengthPrefixed(stream, "nonce", EnvelopeCrypto.NonceSizeBytes, EnvelopeCrypto.NonceSizeBytes);
        var tag = ReadLengthPrefixed(stream, "tag", EnvelopeCrypto.TagSizeBytes, EnvelopeCrypto.TagSizeBytes);
        var ciphertext = ReadLengthPrefixed(stream, "ciphertext", MaxCiphertextLengthBytes);

        return new ProtectedFilePackage(header, new AesGcmPayload(nonce, ciphertext, tag));
    }

    private static byte[] ReadLengthPrefixed(Stream stream, string fieldName, int maxLength)
    {
        return ReadLengthPrefixed(stream, fieldName, minLength: 0, maxLength);
    }

    private static byte[] ReadLengthPrefixed(Stream stream, string fieldName, int minLength, int maxLength)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        ReadExactly(stream, lengthBytes);

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (length < 0)
        {
            throw new InvalidDataException($"Protected file {fieldName} length cannot be negative.");
        }

        if (length < minLength || length > maxLength)
        {
            var requirement = minLength == maxLength
                ? $"must be {minLength} bytes"
                : $"must be between {minLength} and {maxLength} bytes";

            throw new InvalidDataException($"Protected file {fieldName} length {requirement}.");
        }

        var value = new byte[length];
        ReadExactly(stream, value);
        return value;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Protected file ended unexpectedly.", exception);
        }
    }
}
