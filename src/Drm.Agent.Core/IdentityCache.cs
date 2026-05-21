using System.Text.Json;

namespace Drm.Agent.Core;

/// <summary>
/// What the tray first-run dialog needs to remember between launches so
/// the user only types their work email once.  Everything else
/// (server URL, tenant, user GUID, display name, default template)
/// is filled in from the /api/agent/discover response.
///
/// ServerUrl is included even though the MSI also writes it to a
/// registry key: keeping it here lets sysadmins point a single
/// installed agent at a non-default server (dev sandbox, staging,
/// on-prem) without re-running the MSI.
/// </summary>
public sealed record AgentIdentityCacheEntry(
    Guid TenantId,
    Guid UserId,
    string Email,
    string DisplayName,
    Uri ServerUrl,
    Guid? DefaultPolicyTemplateId,
    int DefaultExpiryDays,
    DateTimeOffset SavedAtUtc);

/// <summary>
/// Persistence contract for the identity cache.  The cross-platform
/// implementation here serialises to JSON and writes the file
/// PLAINTEXT — fine for tests and the Linux/Mac dev path.  Production
/// Windows builds wrap this with DPAPI encryption (per-user scope) via
/// DpapiIdentityCache in Drm.Agent.Tray.Windows.
/// </summary>
public interface IIdentityCache
{
    Task<AgentIdentityCacheEntry?> ReadAsync(CancellationToken cancellationToken);

    Task WriteAsync(AgentIdentityCacheEntry entry, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Plaintext JSON identity cache — the cross-platform default.
/// Suitable for unit tests, for the Linux CLI flow, and as the inner
/// payload formatter for the Windows DPAPI wrapper.
///
/// Atomic write: serialise to a sibling .tmp file then File.Move so a
/// crash mid-write never leaves a half-truncated cache that the next
/// startup misinterprets as "user has signed out" and re-prompts.
/// </summary>
public sealed class JsonFileIdentityCache : IIdentityCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string filePath;

    public JsonFileIdentityCache(string filePath)
    {
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public async Task<AgentIdentityCacheEntry?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<AgentIdentityCacheEntry>(
            stream, JsonOptions, cancellationToken);
    }

    public async Task WriteAsync(AgentIdentityCacheEntry entry, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
        }

        // File.Move(... overwrite: true) is atomic on the same volume.
        // If the destination doesn't exist yet, the overwrite flag is
        // a no-op.
        File.Move(tempPath, filePath, overwrite: true);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}
