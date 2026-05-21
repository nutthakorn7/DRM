using System.Text.Json;

namespace Drm.Agent.Core;

/// <summary>
/// Stage 15 — keep the last N recipient emails the agent sent to so the
/// sender can pick from a dropdown instead of retyping. Real customers
/// send to the same 10-20 people every day; a dropdown removes the
/// most common Quick Send keystroke.
/// </summary>
public interface IRecentRecipientsStore
{
    Task RememberAsync(string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);
}

public sealed record RecentRecipient(string Email, DateTimeOffset LastUsedUtc);

public sealed class JsonRecentRecipientsStore(string path, int capacity = 20) : IRecentRecipientsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task RememberAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = NormalizeEmail(email);

        var records = await ReadAsync(cancellationToken);
        // Dedup case-insensitive so "Malee@XYZ.com" and "malee@xyz.com"
        // don't double up.
        records.RemoveAll(record => string.Equals(NormalizeEmail(record.Email), normalized, StringComparison.OrdinalIgnoreCase));
        records.Insert(0, new RecentRecipient(normalized, DateTimeOffset.UtcNow));

        if (records.Count > capacity)
        {
            records.RemoveRange(capacity, records.Count - capacity);
        }

        await WriteAsync(records, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        var records = await ReadAsync(cancellationToken);
        return records.OrderByDescending(r => r.LastUsedUtc).Select(r => r.Email).ToList();
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private async Task<List<RecentRecipient>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<RecentRecipient>>(stream, JsonOptions, cancellationToken)
                ?? [];
        }
        catch (JsonException)
        {
            // Corrupt file shouldn't break the agent. Drop it; the next
            // RememberAsync will rebuild it cleanly.
            return [];
        }
    }

    private async Task WriteAsync(IReadOnlyList<RecentRecipient> records, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(records, JsonOptions), cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }
}
