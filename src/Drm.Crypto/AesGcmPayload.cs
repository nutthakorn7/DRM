namespace Drm.Crypto;

public sealed class AesGcmPayload
{
    private readonly byte[] _nonce;
    private readonly byte[] _ciphertext;
    private readonly byte[] _tag;

    public AesGcmPayload(byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(tag);

        _nonce = (byte[])nonce.Clone();
        _ciphertext = (byte[])ciphertext.Clone();
        _tag = (byte[])tag.Clone();
    }

    public byte[] Nonce => (byte[])_nonce.Clone();

    public byte[] Ciphertext => (byte[])_ciphertext.Clone();

    public byte[] Tag => (byte[])_tag.Clone();
}
