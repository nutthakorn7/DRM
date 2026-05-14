using Drm.Crypto;
using FluentAssertions;

namespace Drm.Crypto.Tests;

public sealed class EnvelopeCryptoTests
{
    [Fact]
    public void Encrypt_then_decrypt_round_trips_payload()
    {
        var key = EnvelopeCrypto.GenerateKey();
        var plaintext = "sensitive pdf bytes"u8.ToArray();

        var encrypted = EnvelopeCrypto.Encrypt(plaintext, key, "file:123"u8.ToArray());
        var decrypted = EnvelopeCrypto.Decrypt(encrypted, key, "file:123"u8.ToArray());

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void Decrypt_rejects_wrong_associated_data()
    {
        var key = EnvelopeCrypto.GenerateKey();
        var encrypted = EnvelopeCrypto.Encrypt("payload"u8.ToArray(), key, "file:123"u8.ToArray());

        var action = () => EnvelopeCrypto.Decrypt(encrypted, key, "file:456"u8.ToArray());

        action.Should().Throw<System.Security.Cryptography.AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Payload_properties_return_defensive_copies()
    {
        var key = EnvelopeCrypto.GenerateKey();
        var plaintext = "payload"u8.ToArray();
        var encrypted = EnvelopeCrypto.Encrypt(plaintext, key, "file:123"u8.ToArray());

        encrypted.Nonce[0] ^= 0xff;
        encrypted.Ciphertext[0] ^= 0xff;
        encrypted.Tag[0] ^= 0xff;

        var decrypted = EnvelopeCrypto.Decrypt(encrypted, key, "file:123"u8.ToArray());

        decrypted.Should().Equal(plaintext);
    }
}
