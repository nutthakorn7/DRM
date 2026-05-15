using System.Text.Json;
using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class AgentAuditQueueTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Audit_queue_writes_hash_chained_entries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        var queue = new AgentAuditQueue(path, new RecordingAuditUploader());
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);
        await queue.EnqueueAsync(identity, "access_allowed", "allowed", Guid.NewGuid(), CancellationToken.None);

        var lines = File.ReadAllLines(path);
        lines.Should().HaveCount(2);
        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);

        first.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        first.RootElement.GetProperty("previousHash").ValueKind.Should().Be(JsonValueKind.Null);
        first.RootElement.GetProperty("entryHash").GetString().Should().NotBeNullOrWhiteSpace();
        first.RootElement.GetProperty("record").GetProperty("eventType").GetString().Should().Be("agent_heartbeat");

        second.RootElement.GetProperty("previousHash").GetString()
            .Should().Be(first.RootElement.GetProperty("entryHash").GetString());
        second.RootElement.GetProperty("record").GetProperty("eventType").GetString().Should().Be("access_allowed");
    }

    [Fact]
    public async Task Audit_queue_stops_flush_when_hash_chain_entry_is_tampered()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        var uploader = new RecordingAuditUploader();
        var queue = new AgentAuditQueue(path, uploader);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);
        await queue.EnqueueAsync(identity, "access_allowed", "allowed", Guid.NewGuid(), CancellationToken.None);

        var lines = File.ReadAllLines(path);
        lines[1] = lines[1].Replace("access_allowed", "access_denied", StringComparison.Ordinal);
        File.WriteAllLines(path, lines);

        await queue.FlushAsync(CancellationToken.None);

        uploader.UploadedEvents.Should().ContainSingle(record => record.EventType == "agent_heartbeat");
        File.ReadAllLines(path).Should().ContainSingle().Which.Should().Be(lines[1]);
    }

    [Fact]
    public async Task Audit_queue_flushes_legacy_raw_record_lines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        var uploader = new RecordingAuditUploader();
        var queue = new AgentAuditQueue(path, uploader);
        var record = new AgentAuditRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "access_allowed",
            "allowed",
            DateTimeOffset.UtcNow);

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, JsonOptions));

        await queue.FlushAsync(CancellationToken.None);

        File.Exists(path).Should().BeFalse();
        uploader.UploadedEvents.Should().ContainSingle(uploaded => uploaded.EventType == "access_allowed");
    }

    [Fact]
    public async Task Audit_queue_keeps_failed_events_and_removes_uploaded_events()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        var uploader = new RecordingAuditUploader(failFirstUpload: true);
        var queue = new AgentAuditQueue(path, uploader);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);
        await queue.FlushAsync(CancellationToken.None);

        File.ReadAllLines(path).Should().HaveCount(1);
        uploader.UploadedEvents.Should().BeEmpty();

        await queue.FlushAsync(CancellationToken.None);

        File.Exists(path).Should().BeFalse();
        uploader.UploadedEvents.Should().ContainSingle(record =>
            record.TenantId == identity.TenantId &&
            record.UserId == identity.UserId &&
            record.DeviceId == identity.DeviceId &&
            record.EventType == "agent_heartbeat" &&
            record.ReasonCode == "online");
    }

    [Fact]
    public async Task Audit_queue_preserves_remaining_events_after_later_upload_failure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        var uploader = new RecordingAuditUploader(failOnUploadNumber: 2);
        var queue = new AgentAuditQueue(path, uploader);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);
        await queue.EnqueueAsync(identity, "access_allowed", "allowed", Guid.NewGuid(), CancellationToken.None);
        await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);

        await queue.FlushAsync(CancellationToken.None);

        uploader.UploadedEvents.Should().ContainSingle(record => record.EventType == "agent_heartbeat");
        File.ReadAllLines(path).Should().HaveCount(2);
    }

    private sealed class RecordingAuditUploader : IAgentAuditUploader
    {
        private readonly bool failFirstUpload;
        private readonly int? failOnUploadNumber;
        private int attempts;

        public RecordingAuditUploader(bool failFirstUpload = false, int? failOnUploadNumber = null)
        {
            this.failFirstUpload = failFirstUpload;
            this.failOnUploadNumber = failOnUploadNumber;
        }

        public List<AgentAuditRecord> UploadedEvents { get; } = [];

        public Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken)
        {
            attempts++;
            if ((failFirstUpload && attempts == 1) || attempts == failOnUploadNumber)
            {
                throw new HttpRequestException("upload failed");
            }

            UploadedEvents.Add(record);
            return Task.CompletedTask;
        }
    }
}
