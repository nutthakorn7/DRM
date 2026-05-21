using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class JsonRecentRecipientsStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"drm-recipients-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task ListAsync_returns_empty_when_file_does_not_exist()
    {
        var store = new JsonRecentRecipientsStore(path);
        var list = await store.ListAsync(CancellationToken.None);
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task RememberAsync_persists_and_returns_in_MRU_order()
    {
        var store = new JsonRecentRecipientsStore(path);

        await store.RememberAsync("alice@example.com", default);
        await store.RememberAsync("bob@example.com", default);
        await store.RememberAsync("carol@example.com", default);

        var list = await store.ListAsync(default);
        list.Should().Equal("carol@example.com", "bob@example.com", "alice@example.com");
    }

    [Fact]
    public async Task RememberAsync_deduplicates_case_insensitively_and_promotes_to_top()
    {
        var store = new JsonRecentRecipientsStore(path);

        await store.RememberAsync("alice@example.com", default);
        await store.RememberAsync("bob@example.com", default);
        await store.RememberAsync("ALICE@example.com", default);

        var list = await store.ListAsync(default);
        list.Should().Equal("alice@example.com", "bob@example.com");
    }

    [Fact]
    public async Task RememberAsync_trims_whitespace_before_storing()
    {
        var store = new JsonRecentRecipientsStore(path);

        await store.RememberAsync("  alice@example.com  ", default);

        var list = await store.ListAsync(default);
        list.Should().ContainSingle().Which.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task RememberAsync_caps_history_at_configured_capacity()
    {
        var store = new JsonRecentRecipientsStore(path, capacity: 3);

        for (var index = 0; index < 5; index++)
        {
            await store.RememberAsync($"user{index}@example.com", default);
        }

        var list = await store.ListAsync(default);
        list.Should().HaveCount(3);
        list.Should().Equal("user4@example.com", "user3@example.com", "user2@example.com");
    }

    [Fact]
    public async Task RememberAsync_survives_a_corrupt_file_by_resetting()
    {
        await File.WriteAllTextAsync(path, "not json {{ broken", default);
        var store = new JsonRecentRecipientsStore(path);

        await store.RememberAsync("alice@example.com", default);

        var list = await store.ListAsync(default);
        list.Should().Equal("alice@example.com");
    }

    [Fact]
    public async Task RememberAsync_persists_across_store_instances()
    {
        var first = new JsonRecentRecipientsStore(path);
        await first.RememberAsync("alice@example.com", default);

        var second = new JsonRecentRecipientsStore(path);
        var list = await second.ListAsync(default);

        list.Should().Equal("alice@example.com");
    }

    [Fact]
    public async Task RememberAsync_throws_on_blank_email()
    {
        var store = new JsonRecentRecipientsStore(path);
        var act = () => store.RememberAsync("   ", default);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
