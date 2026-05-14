using System.Security.Cryptography;

namespace Drm.Crypto;

public static class EnvelopeCrypto
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeySizeBytes);

    public static AesGcmPayload Encrypt(byte[] plaintext, byte[] key, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKey(key);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new AesGcmPayload(nonce, ciphertext, tag);
    }

    public static byte[] Decrypt(AesGcmPayload payload, byte[] key, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateKey(key);
        ValidatePayload(payload);

        var plaintext = new byte[payload.Ciphertext.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);

        return plaintext;
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"Key must be {KeySizeBytes} bytes.", nameof(key));
        }
    }

    private static void ValidatePayload(AesGcmPayload payload)
    {
        if (payload.Nonce.Length != NonceSizeBytes)
        {
            throw new ArgumentException($"Nonce must be {NonceSizeBytes} bytes.", nameof(payload));
        }

        if (payload.Tag.Length != TagSizeBytes)
        {
            throw new ArgumentException($"Tag must be {TagSizeBytes} bytes.", nameof(payload));
        }
    }
}
