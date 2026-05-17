using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Drm.Crypto;

namespace Drm.Server.Tests;

public sealed class SecureContainerTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(SecureContainer.KeySize);

    [Fact]
    public void Pack_then_unpack_round_trips_files_and_manifest()
    {
        var containerId = Guid.NewGuid();
        var entries = new List<SecureContainerEntry>
        {
            new("doc.txt", Encoding.UTF8.GetBytes("hello")),
            new("images/banner.bin", new byte[] { 1, 2, 3, 4, 5 }),
            new("nested/sub/asset.dat", Encoding.UTF8.GetBytes("nested file payload"))
        };
        var key = Key();

        var container = SecureContainer.Pack(containerId, entries, key);
        container.AsSpan(0, SecureContainer.Magic.Length).ToArray().Should().Equal(SecureContainer.Magic);

        var unpacked = SecureContainer.Unpack(container, key);
        unpacked.ContainerId.Should().Be(containerId);
        unpacked.Entries.Should().HaveCount(3);
        unpacked.Entries.Single(e => e.RelativePath == "doc.txt").Content.Should().Equal(entries[0].Content);
        unpacked.Manifest.Entries.Select(e => e.RelativePath)
            .Should().BeEquivalentTo(new[] { "doc.txt", "images/banner.bin", "nested/sub/asset.dat" });
    }

    [Fact]
    public void Unpack_with_wrong_key_throws_authentication_failure()
    {
        var containerId = Guid.NewGuid();
        var entries = new List<SecureContainerEntry> { new("a.txt", Encoding.UTF8.GetBytes("x")) };
        var container = SecureContainer.Pack(containerId, entries, Key());

        var action = () => SecureContainer.Unpack(container, Key());
        action.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Unpack_with_wrong_magic_throws_invalid_data()
    {
        var bytes = new byte[64];
        var action = () => SecureContainer.Unpack(bytes, Key());
        action.Should().Throw<InvalidDataException>().WithMessage("*magic mismatch*");
    }

    [Fact]
    public void Pack_rejects_invalid_key_size()
    {
        var entries = new List<SecureContainerEntry> { new("x.txt", new byte[] { 1 }) };
        var action = () => SecureContainer.Pack(Guid.NewGuid(), entries, new byte[8]);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeriveKey_is_deterministic_for_same_passphrase_and_salt()
    {
        var salt = RandomNumberGenerator.GetBytes(SecureContainer.SaltSize);
        var keyA = SecureContainer.DeriveKey("hunter2", salt);
        var keyB = SecureContainer.DeriveKey("hunter2", salt);
        keyA.Should().Equal(keyB);
        keyA.Length.Should().Be(SecureContainer.KeySize);

        var differentSalt = RandomNumberGenerator.GetBytes(SecureContainer.SaltSize);
        var keyDifferent = SecureContainer.DeriveKey("hunter2", differentSalt);
        keyA.Should().NotEqual(keyDifferent);
    }

    [Fact]
    public void Pack_with_explicit_salt_writes_v2_header_with_salt_readable_via_TryReadSalt()
    {
        var containerId = Guid.NewGuid();
        var salt = RandomNumberGenerator.GetBytes(SecureContainer.SaltSize);
        var key = SecureContainer.DeriveKey("pwd", salt);
        var entries = new List<SecureContainerEntry> { new("x.txt", Encoding.UTF8.GetBytes("data")) };

        var container = SecureContainer.Pack(containerId, entries, key, salt);
        container[SecureContainer.Magic.Length].Should().Be(SecureContainer.Version);

        var readSalt = SecureContainer.TryReadSalt(container);
        readSalt.Should().NotBeNull();
        readSalt.Should().Equal(salt);

        var unpacked = SecureContainer.Unpack(container, key);
        unpacked.ContainerId.Should().Be(containerId);
    }

    [Fact]
    public void Unpack_rejects_zip_slip_relative_paths_in_entries()
    {
        var containerId = Guid.NewGuid();
        var salt = RandomNumberGenerator.GetBytes(SecureContainer.SaltSize);
        var key = SecureContainer.DeriveKey("pwd", salt);
        var malicious = new List<SecureContainerEntry>
        {
            new("../evil.exe", new byte[] { 1, 2, 3 })
        };

        var action = () => SecureContainer.Pack(containerId, malicious, key, salt);
        action.Should().Throw<InvalidDataException>().WithMessage("*..*");
    }

    [Fact]
    public void Unpack_rejects_absolute_path_entries()
    {
        var containerId = Guid.NewGuid();
        var salt = RandomNumberGenerator.GetBytes(SecureContainer.SaltSize);
        var key = SecureContainer.DeriveKey("pwd", salt);
        var malicious = new List<SecureContainerEntry>
        {
            new("/etc/passwd", new byte[] { 1 })
        };

        var action = () => SecureContainer.Pack(containerId, malicious, key, salt);
        action.Should().Throw<InvalidDataException>();
    }
}
