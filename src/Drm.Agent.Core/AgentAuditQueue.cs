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

        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
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

        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AgentAuditRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<AgentAuditRecord>(line, JsonOptions);
            }
            catch (JsonException)
            {
                remainingLines.AddRange(lines.Skip(index).Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
                break;
            }

            if (record is null)
            {
                remainingLines.AddRange(lines.Skip(index).Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
                break;
            }

            try
            {
                await uploader.UploadAuditAsync(record, cancellationToken);
            }
            catch (HttpRequestException)
            {
                remainingLines.Add(line);
                remainingLines.AddRange(lines.Skip(index + 1).Where(candidate => !string.IsNullOrWhiteSpace(candidate)));
                break;
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
}
