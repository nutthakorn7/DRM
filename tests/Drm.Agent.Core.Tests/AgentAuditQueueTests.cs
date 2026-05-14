using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class AgentAuditQueueTests
{
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
