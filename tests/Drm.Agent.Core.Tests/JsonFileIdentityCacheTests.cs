using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

/// <summary>
/// Tests for the cross-platform JsonFileIdentityCache used by the
/// tray first-run flow. Windows production uses DpapiIdentityCache
/// which wraps this same JSON serialization with DPAPI encryption;
/// those tests live in the Drm.Agent.Tray.Windows test project (when
/// it exists) because DPAPI is Windows-only.
/// </summary>
public sealed class JsonFileIdentityCacheTests : IDisposable
{
    private readonly string tempPath = Path.Combine(
        Path.GetTempPath(),
        $"drm-id-cache-test-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Read_returns_null_when_file_is_missing()
    {
        var cache = new JsonFileIdentityCache(tempPath);

        var entry = await cache.ReadAsync(CancellationToken.None);

        entry.Should().BeNull(
            "no cache file is the first-launch state — must not throw");
    }

    [Fact]
    public async Task Write_then_read_round_trips_every_field()
    {
        var cache = new JsonFileIdentityCache(tempPath);
        var original = new AgentIdentityCacheEntry(
            TenantId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            UserId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Email: "alice@acme.test",
            DisplayName: "Alice Tester",
            ServerUrl: new Uri("https://drm.acme.test/"),
            DefaultPolicyTemplateId: Guid.Parse("00000000-0000-0000-0000-000000000003"),
            DefaultExpiryDays: 14,
            SavedAtUtc: new DateTimeOffset(2026, 5, 21, 9, 0, 0, TimeSpan.Zero));

        await cache.WriteAsync(original, CancellationToken.None);
        var read = await cache.ReadAsync(CancellationToken.None);

        read.Should().NotBeNull();
        read.Should().Be(original,
            "the record-equality semantics let us assert all fields in one shot");
    }

    [Fact]
    public async Task Write_creates_missing_parent_directories()
    {
        var nested = Path.Combine(
            Path.GetTempPath(),
            $"drm-id-cache-nested-{Guid.NewGuid():N}",
            "subdir",
            "identity.json");
        var cache = new JsonFileIdentityCache(nested);
        var entry = SampleEntry();

        try
        {
            await cache.WriteAsync(entry, CancellationToken.None);
            File.Exists(nested).Should().BeTrue();
        }
        finally
        {
            // Cleanup. Walk up so leftover empty dirs don't accumulate.
            File.Delete(nested);
            Directory.Delete(Path.GetDirectoryName(nested)!);
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(nested)!)!);
        }
    }

    [Fact]
    public async Task Write_is_atomic_via_rename()
    {
        var cache = new JsonFileIdentityCache(tempPath);
        await cache.WriteAsync(SampleEntry(), CancellationToken.None);

        // After write, no .tmp file should be left lying around — the
        // file is supposed to be moved into place, not copied.
        File.Exists(tempPath + ".tmp").Should().BeFalse(
            "atomic write should rename the temp file, not leave it behind");
    }

    [Fact]
    public async Task Clear_removes_the_file()
    {
        var cache = new JsonFileIdentityCache(tempPath);
        await cache.WriteAsync(SampleEntry(), CancellationToken.None);
        File.Exists(tempPath).Should().BeTrue();

        await cache.ClearAsync(CancellationToken.None);

        File.Exists(tempPath).Should().BeFalse();
    }

    [Fact]
    public async Task Clear_does_not_throw_when_already_absent()
    {
        var cache = new JsonFileIdentityCache(tempPath);

        var act = async () => await cache.ClearAsync(CancellationToken.None);

        await act.Should().NotThrowAsync(
            "the 'sign out, then sign out again' UX must not blow up");
    }

    private static AgentIdentityCacheEntry SampleEntry() => new(
        TenantId: Guid.NewGuid(),
        UserId: Guid.NewGuid(),
        Email: "sample@example.test",
        DisplayName: "Sample User",
        ServerUrl: new Uri("https://drm.example/"),
        DefaultPolicyTemplateId: null,
        DefaultExpiryDays: 7,
        SavedAtUtc: DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
        if (File.Exists(tempPath + ".tmp")) File.Delete(tempPath + ".tmp");
    }
}
