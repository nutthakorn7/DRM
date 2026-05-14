using System.Text.Json;

namespace Drm.Agent.Core;

public sealed record ProtectedFileInventoryEntry(
    Guid TenantId,
    Guid FileId,
    string Path,
    DateTimeOffset AddedAtUtc);

public interface IProtectedFileInventory
{
    Task UpsertAsync(ProtectedFileInventoryEntry entry, CancellationToken cancellationToken);

    Task<ProtectedFileInventoryEntry?> FindAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);

    Task RemoveAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);
}

public sealed class JsonProtectedFileInventory(string path) : IProtectedFileInventory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task UpsertAsync(ProtectedFileInventoryEntry entry, CancellationToken cancellationToken)
    {
        var entries = await ReadEntriesAsync(cancellationToken);
        entries.RemoveAll(candidate => candidate.TenantId == entry.TenantId && candidate.FileId == entry.FileId);
        entries.Add(entry);
        await WriteEntriesAsync(entries, cancellationToken);
    }

    public async Task<ProtectedFileInventoryEntry?> FindAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        var entries = await ReadEntriesAsync(cancellationToken);
        return entries.SingleOrDefault(entry => entry.TenantId == tenantId && entry.FileId == fileId);
    }

    public async Task RemoveAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        var entries = await ReadEntriesAsync(cancellationToken);
        entries.RemoveAll(entry => entry.TenantId == tenantId && entry.FileId == fileId);
        await WriteEntriesAsync(entries, cancellationToken);
    }

    private async Task<List<ProtectedFileInventoryEntry>> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ProtectedFileInventoryEntry>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }

    private async Task WriteEntriesAsync(IReadOnlyList<ProtectedFileInventoryEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }
}
