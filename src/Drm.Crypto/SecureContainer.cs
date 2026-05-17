using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Drm.Crypto;

/// <summary>
/// Sealed folder container ("セキュアコンテナ" / FinalCode Secure Container).
///
/// Wraps an entire directory tree in a single .drmcontainer file. Files inside
/// keep their relative paths and can reference each other (Ai -> Ps -> Ps style
/// asset chains). The whole archive is sealed with AES-GCM so the container
/// cannot be opened without the per-container key.
///
/// File layout:
///   [magic            ]   8 bytes  "DRMSCONT"
///   [version          ]   1 byte   = 1
///   [container id     ]  16 bytes  GUID (little-endian)
///   [nonce            ]  12 bytes  AES-GCM nonce
///   [tag              ]  16 bytes  AES-GCM auth tag
///   [ciphertext length]   4 bytes  little-endian
///   [ciphertext       ]   N bytes  AES-GCM ciphertext of the inner ZIP archive
///
/// The inner ZIP archive contains:
///   - manifest.json with the list of contained files
///   - one entry per source file at its original relative path
/// </summary>
public static class SecureContainer
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("DRMSCONT");
    public const byte Version = 1;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32;

    public static byte[] Pack(Guid containerId, IReadOnlyList<SecureContainerEntry> entries, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (key is null || key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be exactly {KeySize} bytes.", nameof(key));
        }

        var manifest = new SecureContainerManifest(
            containerId,
            entries.Select(e => new SecureContainerManifestEntry(e.RelativePath, e.Content.LongLength)).ToList(),
            DateTimeOffset.UtcNow);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOpts);

        byte[] zipBytes;
        using (var zipStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddEntry(archive, "manifest.json", manifestBytes);
                foreach (var entry in entries)
                {
                    AddEntry(archive, entry.RelativePath, entry.Content);
                }
            }
            zipBytes = zipStream.ToArray();
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[zipBytes.Length];
        var tag = new byte[TagSize];
        using (var gcm = new AesGcm(key, TagSize))
        {
            gcm.Encrypt(nonce, zipBytes, ciphertext, tag, associatedData: containerId.ToByteArray());
        }

        var result = new byte[
            Magic.Length + 1 + 16 + NonceSize + TagSize + 4 + ciphertext.Length];
        var offset = 0;
        Buffer.BlockCopy(Magic, 0, result, offset, Magic.Length); offset += Magic.Length;
        result[offset++] = Version;
        Buffer.BlockCopy(containerId.ToByteArray(), 0, result, offset, 16); offset += 16;
        Buffer.BlockCopy(nonce, 0, result, offset, NonceSize); offset += NonceSize;
        Buffer.BlockCopy(tag, 0, result, offset, TagSize); offset += TagSize;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset, 4), ciphertext.Length); offset += 4;
        Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);

        return result;
    }

    public static SecureContainerUnpacked Unpack(byte[] container, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (key is null || key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be exactly {KeySize} bytes.", nameof(key));
        }

        var headerSize = Magic.Length + 1 + 16 + NonceSize + TagSize + 4;
        if (container.Length < headerSize)
        {
            throw new InvalidDataException("Container is too small.");
        }
        if (!container.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Container magic mismatch.");
        }
        if (container[Magic.Length] != Version)
        {
            throw new InvalidDataException($"Unsupported container version {container[Magic.Length]}.");
        }

        var offset = Magic.Length + 1;
        var containerIdBytes = container.AsSpan(offset, 16).ToArray(); offset += 16;
        var containerId = new Guid(containerIdBytes);
        var nonce = container.AsSpan(offset, NonceSize).ToArray(); offset += NonceSize;
        var tag = container.AsSpan(offset, TagSize).ToArray(); offset += TagSize;
        var ciphertextLength = BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(offset, 4)); offset += 4;
        if (ciphertextLength < 0 || offset + ciphertextLength > container.Length)
        {
            throw new InvalidDataException("Container ciphertext length out of bounds.");
        }
        var ciphertext = container.AsSpan(offset, ciphertextLength).ToArray();

        var plaintext = new byte[ciphertext.Length];
        using (var gcm = new AesGcm(key, TagSize))
        {
            gcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData: containerIdBytes);
        }

        SecureContainerManifest? manifest = null;
        var unpacked = new List<SecureContainerEntry>();
        using (var zipStream = new MemoryStream(plaintext))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false))
        {
            foreach (var zipEntry in archive.Entries)
            {
                using var memory = new MemoryStream();
                using (var src = zipEntry.Open())
                {
                    src.CopyTo(memory);
                }
                var bytes = memory.ToArray();
                if (string.Equals(zipEntry.FullName, "manifest.json", StringComparison.Ordinal))
                {
                    manifest = JsonSerializer.Deserialize<SecureContainerManifest>(bytes, JsonOpts);
                }
                else
                {
                    unpacked.Add(new SecureContainerEntry(zipEntry.FullName, bytes));
                }
            }
        }

        if (manifest is null)
        {
            throw new InvalidDataException("Container manifest is missing.");
        }
        if (manifest.ContainerId != containerId)
        {
            throw new InvalidDataException("Container manifest ID mismatch.");
        }

        return new SecureContainerUnpacked(containerId, manifest, unpacked);
    }

    public static byte[] DeriveKey(string passphrase, Guid containerId)
    {
        var salt = containerId.ToByteArray();
        return Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(passphrase),
            salt: salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySize);
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content, 0, content.Length);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

public sealed record SecureContainerEntry(string RelativePath, byte[] Content);

public sealed record SecureContainerManifest(
    Guid ContainerId,
    IReadOnlyList<SecureContainerManifestEntry> Entries,
    DateTimeOffset CreatedAtUtc);

public sealed record SecureContainerManifestEntry(string RelativePath, long Size);

public sealed record SecureContainerUnpacked(
    Guid ContainerId,
    SecureContainerManifest Manifest,
    IReadOnlyList<SecureContainerEntry> Entries);
