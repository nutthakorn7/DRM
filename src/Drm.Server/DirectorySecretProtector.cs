using System.Security.Cryptography;
using System.Text;
using Drm.Crypto;

namespace Drm.Server;

/// <summary>
/// Encrypts directory-sync secrets (the LDAP bind password) at rest with the same AES-256-GCM
/// master key used for file-key wrapping (<c>Drm:KeyWrapping:MasterKeyBase64</c>) — so no new key
/// to manage. Tenant-scoped AAD binds ciphertext to its tenant. Stored form is
/// <c>drmenc1:b64(nonce).b64(ciphertext).b64(tag)</c>; <see cref="Unprotect"/> passes through any
/// value without the prefix unchanged, so a value provisioned before encryption (legacy plaintext)
/// keeps working until it is next saved.
/// </summary>
public interface IDirectorySecretProtector
{
    string Protect(Guid tenantId, string plaintext);
    string Unprotect(Guid tenantId, string stored);
}

public sealed class DirectorySecretProtector(IConfiguration configuration) : IDirectorySecretProtector
{
    private const string Prefix = "drmenc1:";
    private static readonly byte[] DevelopmentMasterKey =
        SHA256.HashData(Encoding.UTF8.GetBytes("DRM development file-key wrapping master key"));

    public string Protect(Guid tenantId, string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var key = DeriveTenantKey(tenantId);
        var p = EnvelopeCrypto.Encrypt(Encoding.UTF8.GetBytes(plaintext), key, Aad(tenantId));
        return Prefix +
            Convert.ToBase64String(p.Nonce) + "." +
            Convert.ToBase64String(p.Ciphertext) + "." +
            Convert.ToBase64String(p.Tag);
    }

    public string Unprotect(Guid tenantId, string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored ?? string.Empty; // legacy plaintext (or empty) — pass through

        var parts = stored[Prefix.Length..].Split('.');
        if (parts.Length != 3)
            throw new InvalidOperationException("Malformed encrypted directory secret.");

        var payload = new AesGcmPayload(
            Convert.FromBase64String(parts[0]),
            Convert.FromBase64String(parts[1]),
            Convert.FromBase64String(parts[2]));
        // Throws CryptographicException on wrong tenant / tampering — we do NOT swallow that.
        return Encoding.UTF8.GetString(EnvelopeCrypto.Decrypt(payload, DeriveTenantKey(tenantId), Aad(tenantId)));
    }

    private byte[] DeriveTenantKey(Guid tenantId)
        => HMACSHA256.HashData(GetMasterKey(), Encoding.UTF8.GetBytes(tenantId.ToString("D")));

    private byte[] GetMasterKey()
    {
        var configured = configuration["Drm:KeyWrapping:MasterKeyBase64"];
        if (string.IsNullOrWhiteSpace(configured)) return DevelopmentMasterKey;
        var decoded = Convert.FromBase64String(configured);
        if (decoded.Length != EnvelopeCrypto.KeySizeBytes)
            throw new InvalidOperationException($"Drm:KeyWrapping:MasterKeyBase64 must decode to {EnvelopeCrypto.KeySizeBytes} bytes.");
        return decoded;
    }

    private static byte[] Aad(Guid tenantId) => Encoding.UTF8.GetBytes($"{tenantId:D}:directory-sync-secret");
}
