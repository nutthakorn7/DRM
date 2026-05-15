using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Drm.Agent.Core;

public interface IAgentAuditQueue
{
    Task EnqueueAsync(
        AgentIdentity identity,
        string eventType,
        string reasonCode,
        Guid? fileId,
        CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);
}

public sealed class AgentAuditQueue(string path, IAgentAuditUploader uploader) : IAgentAuditQueue
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync(
        AgentIdentity identity,
        string eventType,
        string reasonCode,
        Guid? fileId,
        CancellationToken cancellationToken)
    {
        var record = new AgentAuditRecord(
            identity.TenantId,
            identity.UserId,
            identity.DeviceId,
            fileId,
            eventType,
            reasonCode,
            DateTimeOffset.UtcNow);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var previousHash = await TryGetPreviousHashAsync(cancellationToken);
        var entry = new AuditQueueEntry(
            CurrentSchemaVersion,
            previousHash,
            record,
            CalculateEntryHash(CurrentSchemaVersion, previousHash, record));

        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(path, line, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var remainingLines = new List<string>();
        string? previousEntryHash = null;
        var hasPreviousEntryHash = false;

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryReadQueuedRecord(line, hasPreviousEntryHash, previousEntryHash, out var record, out var entryHash))
            {
                remainingLines.AddRange(lines.Skip(index).Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
                break;
            }

            try
            {
                await uploader.UploadAuditAsync(record!, cancellationToken);
            }
            catch (HttpRequestException)
            {
                remainingLines.Add(line);
                remainingLines.AddRange(lines.Skip(index + 1).Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
                break;
            }

            if (entryHash is not null)
            {
                previousEntryHash = entryHash;
                hasPreviousEntryHash = true;
            }
            else
            {
                previousEntryHash = null;
                hasPreviousEntryHash = false;
            }
        }

        if (remainingLines.Count == 0)
        {
            File.Delete(path);
            return;
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllLinesAsync(tempPath, remainingLines, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private async Task<string?> TryGetPreviousHashAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var lastLine = lines.LastOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (lastLine is null)
        {
            return null;
        }

        return TryReadEnvelope(lastLine, requirePreviousHash: false, expectedPreviousHash: null, out _, out var entryHash)
            ? entryHash
            : null;
    }

    private static bool TryReadQueuedRecord(
        string line,
        bool requirePreviousHash,
        string? expectedPreviousHash,
        out AgentAuditRecord? record,
        out string? entryHash)
    {
        record = null;
        entryHash = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("schemaVersion", out _))
            {
                return TryReadEnvelope(line, requirePreviousHash, expectedPreviousHash, out record, out entryHash);
            }
        }

        try
        {
            record = JsonSerializer.Deserialize<AgentAuditRecord>(line, JsonOptions);
            return record is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadEnvelope(
        string line,
        bool requirePreviousHash,
        string? expectedPreviousHash,
        out AgentAuditRecord? record,
        out string? entryHash)
    {
        record = null;
        entryHash = null;

        AuditQueueEntry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<AuditQueueEntry>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (entry is null ||
            entry.SchemaVersion != CurrentSchemaVersion ||
            entry.Record is null ||
            string.IsNullOrWhiteSpace(entry.EntryHash))
        {
            return false;
        }

        if (requirePreviousHash && !string.Equals(entry.PreviousHash, expectedPreviousHash, StringComparison.Ordinal))
        {
            return false;
        }

        var calculatedHash = CalculateEntryHash(entry.SchemaVersion, entry.PreviousHash, entry.Record);
        if (!string.Equals(entry.EntryHash, calculatedHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        record = entry.Record;
        entryHash = entry.EntryHash;
        return true;
    }

    private static string CalculateEntryHash(
        int schemaVersion,
        string? previousHash,
        AgentAuditRecord record)
    {
        var payload = new AuditQueueEntryHashPayload(schemaVersion, previousHash, record);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private sealed record AuditQueueEntry(
        int SchemaVersion,
        string? PreviousHash,
        AgentAuditRecord Record,
        string EntryHash);

    private sealed record AuditQueueEntryHashPayload(
        int SchemaVersion,
        string? PreviousHash,
        AgentAuditRecord Record);
}
