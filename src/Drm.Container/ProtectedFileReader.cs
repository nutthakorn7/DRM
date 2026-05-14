using System.Buffers.Binary;
using System.Text.Json;
using Drm.Crypto;

namespace Drm.Container;

public static class ProtectedFileReader
{
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

        var headerBytes = ReadLengthPrefixed(stream);
        var header = JsonSerializer.Deserialize<ProtectedFileHeader>(headerBytes)
            ?? throw new InvalidDataException("Protected file header is invalid.");

        var nonce = ReadLengthPrefixed(stream);
        var tag = ReadLengthPrefixed(stream);
        var ciphertext = ReadLengthPrefixed(stream);

        return new ProtectedFilePackage(header, new AesGcmPayload(nonce, ciphertext, tag));
    }

    private static byte[] ReadLengthPrefixed(Stream stream)
    {
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        ReadExactly(stream, lengthBytes);

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (length < 0)
        {
            throw new InvalidDataException("Protected file contains a negative length.");
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
