using System.Security.Cryptography;
using System.Text;
using Drm.Crypto;

namespace Drm.Server;

public sealed record WrappedFileKey(string NonceBase64, string CiphertextBase64, string TagBase64);

public interface IFileKeyProtector
{
    WrappedFileKey Wrap(Guid tenantId, Guid fileId, byte[] fileKey);

    byte[] Unwrap(Guid tenantId, Guid fileId, WrappedFileKey wrappedKey);
}

public sealed class FileKeyProtector(IConfiguration configuration) : IFileKeyProtector
{
    private static readonly byte[] DevelopmentMasterKey =
        SHA256.HashData(Encoding.UTF8.GetBytes("DRM development file-key wrapping master key"));

    public WrappedFileKey Wrap(Guid tenantId, Guid fileId, byte[] fileKey)
    {
        var tenantKey = DeriveTenantKey(tenantId);
        var payload = EnvelopeCrypto.Encrypt(fileKey, tenantKey, AssociatedData(tenantId, fileId));

        return new WrappedFileKey(
            Convert.ToBase64String(payload.Nonce),
            Convert.ToBase64String(payload.Ciphertext),
            Convert.ToBase64String(payload.Tag));
    }

    public byte[] Unwrap(Guid tenantId, Guid fileId, WrappedFileKey wrappedKey)
    {
        var tenantKey = DeriveTenantKey(tenantId);
        var payload = new AesGcmPayload(
            Convert.FromBase64String(wrappedKey.NonceBase64),
            Convert.FromBase64String(wrappedKey.CiphertextBase64),
            Convert.FromBase64String(wrappedKey.TagBase64));

        return EnvelopeCrypto.Decrypt(payload, tenantKey, AssociatedData(tenantId, fileId));
    }

    private byte[] DeriveTenantKey(Guid tenantId)
    {
        var masterKey = GetMasterKey();
        return HMACSHA256.HashData(masterKey, Encoding.UTF8.GetBytes(tenantId.ToString("D")));
    }

    private byte[] GetMasterKey()
    {
        var configured = configuration["Drm:KeyWrapping:MasterKeyBase64"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DevelopmentMasterKey;
        }

        var decoded = Convert.FromBase64String(configured);
        if (decoded.Length != EnvelopeCrypto.KeySizeBytes)
        {
            throw new InvalidOperationException($"Drm:KeyWrapping:MasterKeyBase64 must decode to {EnvelopeCrypto.KeySizeBytes} bytes.");
        }

        return decoded;
    }

    private static byte[] AssociatedData(Guid tenantId, Guid fileId)
        => Encoding.UTF8.GetBytes($"{tenantId:D}:{fileId:D}");
}
