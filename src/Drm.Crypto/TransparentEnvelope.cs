using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Drm.Crypto;

/// <summary>
/// Appends a tamper-evident DRM metadata trailer to an existing file without
/// modifying its leading bytes. The original file remains readable by any
/// application that ignores trailing bytes (PDF readers, ZIP-based Office
/// formats, plain-text readers). DRM-aware tools can detect the trailer to
/// surface tenant / file ID / policy.
///
/// Trailer layout (little-endian):
///   [magic]              8 bytes  "DRMTRANS"
///   [version]            1 byte   = 1
///   [metadata length N]  4 bytes
///   [metadata JSON]      N bytes  UTF-8
///   [HMAC-SHA256]        32 bytes over (magic | version | metadata length | metadata JSON) using the secret
///   [trailer total size] 4 bytes  total trailer length excluding this field (lets us locate the trailer)
/// </summary>
public static class TransparentEnvelope
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("DRMTRANS");
    public const byte Version = 1;
    private const int HmacSize = 32;
    private const int VersionSize = 1;
    private const int LengthSize = 4;
    private const int FooterSize = 4;

    public static byte[] AppendTrailer(byte[] original, TransparentMetadata metadata, byte[] hmacSecret)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(hmacSecret);

        var metadataJson = JsonSerializer.SerializeToUtf8Bytes(metadata, MetadataJsonOptions);
        var header = new byte[Magic.Length + VersionSize + LengthSize];
        Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
        header[Magic.Length] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length + VersionSize), metadataJson.Length);

        var hmacInput = new byte[header.Length + metadataJson.Length];
        Buffer.BlockCopy(header, 0, hmacInput, 0, header.Length);
        Buffer.BlockCopy(metadataJson, 0, hmacInput, header.Length, metadataJson.Length);

        byte[] mac;
        using (var hmac = new HMACSHA256(hmacSecret))
        {
            mac = hmac.ComputeHash(hmacInput);
        }

        var trailerBody = new byte[header.Length + metadataJson.Length + HmacSize];
        Buffer.BlockCopy(hmacInput, 0, trailerBody, 0, hmacInput.Length);
        Buffer.BlockCopy(mac, 0, trailerBody, hmacInput.Length, HmacSize);

        var footer = new byte[FooterSize];
        BinaryPrimitives.WriteInt32LittleEndian(footer, trailerBody.Length);

        var result = new byte[original.Length + trailerBody.Length + footer.Length];
        Buffer.BlockCopy(original, 0, result, 0, original.Length);
        Buffer.BlockCopy(trailerBody, 0, result, original.Length, trailerBody.Length);
        Buffer.BlockCopy(footer, 0, result, original.Length + trailerBody.Length, footer.Length);
        return result;
    }

    public static bool TryReadTrailer(
        byte[] file,
        byte[] hmacSecret,
        out TransparentMetadata? metadata,
        out int originalLength)
    {
        metadata = null;
        originalLength = file?.Length ?? 0;

        if (file is null || file.Length < FooterSize + Magic.Length + VersionSize + LengthSize + HmacSize)
        {
            return false;
        }

        var trailerSize = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(file.Length - FooterSize, FooterSize));
        if (trailerSize <= 0 || trailerSize > file.Length - FooterSize)
        {
            return false;
        }

        var trailerStart = file.Length - FooterSize - trailerSize;
        var trailerSpan = file.AsSpan(trailerStart, trailerSize);

        if (!trailerSpan[..Magic.Length].SequenceEqual(Magic))
        {
            return false;
        }

        if (trailerSpan[Magic.Length] != Version)
        {
            return false;
        }

        var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(
            trailerSpan.Slice(Magic.Length + VersionSize, LengthSize));
        var headerSize = Magic.Length + VersionSize + LengthSize;
        if (metadataLength <= 0 || headerSize + metadataLength + HmacSize != trailerSize)
        {
            return false;
        }

        var hmacInputLength = headerSize + metadataLength;
        var hmacInput = trailerSpan[..hmacInputLength].ToArray();
        var providedMac = trailerSpan.Slice(hmacInputLength, HmacSize).ToArray();
        byte[] expectedMac;
        using (var hmac = new HMACSHA256(hmacSecret))
        {
            expectedMac = hmac.ComputeHash(hmacInput);
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedMac, providedMac))
        {
            return false;
        }

        var metadataJson = trailerSpan.Slice(headerSize, metadataLength).ToArray();
        try
        {
            metadata = JsonSerializer.Deserialize<TransparentMetadata>(metadataJson, MetadataJsonOptions);
        }
        catch (JsonException)
        {
            metadata = null;
            return false;
        }

        if (metadata is null)
        {
            return false;
        }

        originalLength = trailerStart;
        return true;
    }

    public static byte[] StripTrailer(byte[] file, byte[] hmacSecret)
    {
        if (TryReadTrailer(file, hmacSecret, out _, out var originalLength))
        {
            var result = new byte[originalLength];
            Buffer.BlockCopy(file, 0, result, 0, originalLength);
            return result;
        }
        return file;
    }

    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

public sealed record TransparentMetadata(
    Guid TenantId,
    Guid FileId,
    Guid OwnerUserId,
    string ContentType,
    string OriginalFileName,
    DateTimeOffset RegisteredAtUtc,
    Guid? PolicyTemplateId);
