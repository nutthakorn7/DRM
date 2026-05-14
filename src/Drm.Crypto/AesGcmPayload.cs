namespace Drm.Crypto;

public sealed record AesGcmPayload(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
