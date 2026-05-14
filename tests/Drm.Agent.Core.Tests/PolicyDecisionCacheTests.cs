using Drm.Agent.Core;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class PolicyDecisionCacheTests
{
    [Fact]
    public async Task Json_policy_cache_returns_valid_allowed_decision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var cache = new JsonPolicyDecisionCache(path);
        var key = new PolicyDecisionCacheKey(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Permission.View);
        var entry = new CachedPolicyDecision(
            key,
            "{user} {file}",
            Permission.View | Permission.Print,
            DateTimeOffset.UtcNow.AddMinutes(5));

        await cache.StoreAsync(entry, CancellationToken.None);

        var cached = await cache.TryGetAllowedAsync(key, DateTimeOffset.UtcNow, CancellationToken.None);

        cached.Should().BeEquivalentTo(entry);
    }

    [Fact]
    public async Task Json_policy_cache_ignores_expired_decision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var cache = new JsonPolicyDecisionCache(path);
        var key = new PolicyDecisionCacheKey(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Permission.View);

        await cache.StoreAsync(
            new CachedPolicyDecision(
                key,
                "{user}",
                Permission.View,
                DateTimeOffset.UtcNow.AddSeconds(-1)),
            CancellationToken.None);

        var cached = await cache.TryGetAllowedAsync(key, DateTimeOffset.UtcNow, CancellationToken.None);

        cached.Should().BeNull();
    }
}
