using Drm.Agent.Core;
using Drm.Crypto;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class FileKeyStoreTests
{
    [Fact]
    public async Task JsonFileKeyStore_saves_and_loads_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var store = new JsonFileKeyStore(path);
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var key = EnvelopeCrypto.GenerateKey();

        await store.SaveAsync(tenantId, fileId, key, CancellationToken.None);

        var loaded = await store.LoadAsync(tenantId, fileId, CancellationToken.None);

        loaded.Should().Equal(key);
    }

    [Fact]
    public async Task JsonFileKeyStore_returns_null_for_missing_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var store = new JsonFileKeyStore(path);

        var loaded = await store.LoadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task JsonFileKeyStore_replaces_existing_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var store = new JsonFileKeyStore(path);
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var firstKey = EnvelopeCrypto.GenerateKey();
        var secondKey = EnvelopeCrypto.GenerateKey();

        await store.SaveAsync(tenantId, fileId, firstKey, CancellationToken.None);
        await store.SaveAsync(tenantId, fileId, secondKey, CancellationToken.None);

        var loaded = await store.LoadAsync(tenantId, fileId, CancellationToken.None);

        loaded.Should().Equal(secondKey);
    }
}
