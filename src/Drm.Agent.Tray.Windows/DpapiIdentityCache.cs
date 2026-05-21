using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Drm.Agent.Core;

namespace Drm.Agent.Tray.Windows;

/// <summary>
/// Windows-only IIdentityCache that wraps the cross-platform JSON
/// serialization with DPAPI CurrentUser-scope encryption.  An
/// offboarded user's profile rebuild loses access to the file
/// (DPAPI keys are tied to the Windows credential), which is exactly
/// what we want: the next person logging in can't read the previous
/// user's cached zcrDRM identity.
///
/// File location:
///     %LOCALAPPDATA%\zcrDRM\identity.bin
/// (LocalApplicationData, not ApplicationData — we don't want the file
/// to roam via group policy folder redirection, because the DPAPI
/// envelope is bound to the local machine + user.)
///
/// File format:
///   [16 bytes salt][ciphertext]
/// where ciphertext = ProtectedData.Protect(json-bytes, salt,
///                                          DataProtectionScope.CurrentUser).
/// Salt isn't required for DPAPI security per se, but it ensures two
/// different identity payloads written back-to-back produce different
/// ciphertext, which makes diffing the file useful for forensics.
/// </summary>
public sealed class DpapiIdentityCache : IIdentityCache
{
    private const int SaltLengthBytes = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly string filePath;

    public DpapiIdentityCache(string filePath)
    {
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>
    /// Convenience factory: places the cache at the conventional
    /// %LOCALAPPDATA%\zcrDRM\identity.bin location.
    /// </summary>
    public static DpapiIdentityCache Default()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "zcrDRM");
        return new DpapiIdentityCache(Path.Combine(dir, "identity.bin"));
    }

    public async Task<AgentIdentityCacheEntry?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var blob = await File.ReadAllBytesAsync(filePath, cancellationToken);
        if (blob.Length <= SaltLengthBytes)
        {
            // Corrupt or zero-length file — treat as "no cache" so the
            // first-run dialog re-prompts the email, rather than
            // throwing on startup.
            return null;
        }

        var salt = new byte[SaltLengthBytes];
        Buffer.BlockCopy(blob, 0, salt, 0, SaltLengthBytes);
        var cipher = new byte[blob.Length - SaltLengthBytes];
        Buffer.BlockCopy(blob, SaltLengthBytes, cipher, 0, cipher.Length);

        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(cipher, salt, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // DPAPI refusal — the file was written by another Windows
            // user, the user's profile/credentials were rebuilt, or
            // the file is corrupt.  None of those are exceptional
            // enough to throw on startup; treat as "no cache".
            return null;
        }

        return JsonSerializer.Deserialize<AgentIdentityCacheEntry>(plaintext, JsonOptions);
    }

    public async Task WriteAsync(AgentIdentityCacheEntry entry, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var cipher = ProtectedData.Protect(json, salt, DataProtectionScope.CurrentUser);

        var blob = new byte[SaltLengthBytes + cipher.Length];
        Buffer.BlockCopy(salt, 0, blob, 0, SaltLengthBytes);
        Buffer.BlockCopy(cipher, 0, blob, SaltLengthBytes, cipher.Length);

        var tempPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, blob, cancellationToken);
        File.Move(tempPath, filePath, overwrite: true);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}
