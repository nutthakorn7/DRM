using Drm.Agent.Core;
using Drm.Domain;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class AgentHeartbeatWorkflowTests
{
    [Fact]
    public async Task Heartbeat_workflow_registers_device_records_heartbeat_and_flushes_audit_queue()
    {
        var server = new RecordingServerClient();
        var queue = new RecordingAuditQueue();
        var workflow = new AgentHeartbeatWorkflow(server, queue);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await workflow.ReportOnlineAsync(
            identity,
            "WIN-001",
            "Windows 11",
            "0.1.0",
            CancellationToken.None);

        server.RegisteredIdentity.Should().Be(identity);
        server.RecordedHeartbeat.Should().Be((identity, "online", "0.1.0"));
        queue.Enqueued.Should().BeEmpty();
        queue.FlushCount.Should().Be(1);
    }

    private sealed class RecordingServerClient : IDrmServerClient
    {
        public AgentIdentity? RegisteredIdentity { get; private set; }

        public (AgentIdentity Identity, string Status, string AgentVersion)? RecordedHeartbeat { get; private set; }

        public Task RegisterFileAsync(Guid tenantId, Guid fileId, Guid ownerUserId, string contentType, DateTimeOffset expiresAtUtc, Permission permissions, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<OpenDecision> DecideAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, Permission permission, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OpenDecision(true, "allowed", null, Permission.View, DateTimeOffset.UtcNow.AddMinutes(5)));
        }

        public Task<AgentDeviceRegistration> RegisterDeviceAsync(AgentIdentity identity, string hostname, string operatingSystem, string agentVersion, CancellationToken cancellationToken)
        {
            RegisteredIdentity = identity;

            return Task.FromResult(new AgentDeviceRegistration(
                identity.TenantId,
                identity.UserId,
                identity.DeviceId,
                hostname,
                operatingSystem,
                agentVersion,
                "registered",
                DateTimeOffset.UtcNow,
                null));
        }

        public Task<AgentHeartbeat> RecordHeartbeatAsync(AgentIdentity identity, string status, string agentVersion, CancellationToken cancellationToken)
        {
            RecordedHeartbeat = (identity, status, agentVersion);
            return Task.FromResult(new AgentHeartbeat(identity.DeviceId, status, DateTimeOffset.UtcNow));
        }

        public Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentCommand>> GetPendingCommandsAsync(AgentIdentity identity, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AgentCommand>>([]);
        }

        public Task<AgentCommand> CompleteCommandAsync(AgentIdentity identity, Guid commandId, AgentCommandCompletion completion, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingAuditQueue : IAgentAuditQueue
    {
        public List<(AgentIdentity Identity, string EventType, string ReasonCode, Guid? FileId)> Enqueued { get; } = [];

        public int FlushCount { get; private set; }

        public Task EnqueueAsync(AgentIdentity identity, string eventType, string reasonCode, Guid? fileId, CancellationToken cancellationToken)
        {
            Enqueued.Add((identity, eventType, reasonCode, fileId));
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
