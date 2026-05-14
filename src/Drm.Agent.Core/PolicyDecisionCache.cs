using System.Text.Json;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed record PolicyDecisionCacheKey(
    Guid TenantId,
    Guid FileId,
    Guid UserId,
    Guid DeviceId,
    Permission RequestedPermission);

public sealed record CachedPolicyDecision(
    PolicyDecisionCacheKey Key,
    string? WatermarkTemplate,
    Permission AllowedPermissions,
    DateTimeOffset OfflineLeaseExpiresAtUtc);

public interface IPolicyDecisionCache
{
    Task StoreAsync(CachedPolicyDecision decision, CancellationToken cancellationToken);

    Task<CachedPolicyDecision?> TryGetAllowedAsync(
        PolicyDecisionCacheKey key,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);
}

public sealed class JsonPolicyDecisionCache(string path) : IPolicyDecisionCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task StoreAsync(CachedPolicyDecision decision, CancellationToken cancellationToken)
    {
        var entries = await ReadEntriesAsync(cancellationToken);
        entries.RemoveAll(candidate => candidate.Key == decision.Key);
        entries.Add(decision);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    public async Task<CachedPolicyDecision?> TryGetAllowedAsync(
        PolicyDecisionCacheKey key,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var entries = await ReadEntriesAsync(cancellationToken);
        return entries
            .Where(candidate =>
                candidate.Key == key &&
                candidate.OfflineLeaseExpiresAtUtc > atUtc &&
                (candidate.AllowedPermissions & key.RequestedPermission) == key.RequestedPermission)
            .OrderByDescending(candidate => candidate.OfflineLeaseExpiresAtUtc)
            .FirstOrDefault();
    }

    private async Task<List<CachedPolicyDecision>> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<CachedPolicyDecision>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }
}
