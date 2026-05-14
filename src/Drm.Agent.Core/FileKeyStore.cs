using System.Text.Json;

namespace Drm.Agent.Core;

public sealed record FileKeyRecord(
    Guid TenantId,
    Guid FileId,
    string KeyBase64,
    DateTimeOffset StoredAtUtc);

public interface IFileKeyStore
{
    Task SaveAsync(Guid tenantId, Guid fileId, byte[] fileKey, CancellationToken cancellationToken);

    Task<byte[]?> LoadAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken);
}

public sealed class JsonFileKeyStore(string path) : IFileKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task SaveAsync(Guid tenantId, Guid fileId, byte[] fileKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileKey);

        var records = await ReadRecordsAsync(cancellationToken);
        records.RemoveAll(record => record.TenantId == tenantId && record.FileId == fileId);
        records.Add(new FileKeyRecord(
            tenantId,
            fileId,
            Convert.ToBase64String(fileKey),
            DateTimeOffset.UtcNow));

        await WriteRecordsAsync(records, cancellationToken);
    }

    public async Task<byte[]?> LoadAsync(Guid tenantId, Guid fileId, CancellationToken cancellationToken)
    {
        var records = await ReadRecordsAsync(cancellationToken);
        var record = records.SingleOrDefault(candidate => candidate.TenantId == tenantId && candidate.FileId == fileId);
        return record is null ? null : Convert.FromBase64String(record.KeyBase64);
    }

    private async Task<List<FileKeyRecord>> ReadRecordsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<FileKeyRecord>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }

    private async Task WriteRecordsAsync(IReadOnlyList<FileKeyRecord> records, CancellationToken cancellationToken)
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
