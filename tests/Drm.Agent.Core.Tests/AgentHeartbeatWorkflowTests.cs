using System.Net;
using System.Text;
using System.Text.Json.Nodes;
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
        server.RegisterDevicePostureOverloadCallCount.Should().Be(0);
        server.RecordHeartbeatPostureOverloadCallCount.Should().Be(0);
        queue.Enqueued.Should().BeEmpty();
        queue.FlushCount.Should().Be(1);
    }

    [Fact]
    public async Task ReportOnlineAsync_forwards_device_posture()
    {
        var server = new RecordingServerClient();
        var queue = new RecordingAuditQueue();
        var workflow = new AgentHeartbeatWorkflow(server, queue);
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var posture = new AgentDevicePosture(true, "CORP", "CORP\\alice");

        await workflow.ReportOnlineAsync(
            identity,
            "WIN-001",
            "Windows 11",
            "0.1.0",
            posture,
            CancellationToken.None);

        server.RegisteredPosture.Should().Be(posture);
        server.RecordedHeartbeatPosture.Should().Be(posture);
        server.RegisterDevicePostureOverloadCallCount.Should().Be(1);
        server.RecordHeartbeatPostureOverloadCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RegisterDeviceAsync_old_overload_sends_null_posture_fields()
    {
        var handler = new CapturingHandler(RegisterDeviceResponseJson);
        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.test")
        });
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await client.RegisterDeviceAsync(
            identity,
            "WIN-001",
            "Windows 11",
            "0.1.0",
            CancellationToken.None);

        var body = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        AssertNullOrMissing(body, "domainJoined");
        AssertNullOrMissing(body, "domainName");
        AssertNullOrMissing(body, "windowsUser");
    }

    [Fact]
    public async Task RegisterDeviceAsync_posture_overload_sends_posture_fields()
    {
        var handler = new CapturingHandler(RegisterDeviceResponseJson);
        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.test")
        });
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await client.RegisterDeviceAsync(
            identity,
            "WIN-001",
            "Windows 11",
            "0.1.0",
            new AgentDevicePosture(true, "CORP", "CORP\\alice"),
            CancellationToken.None);

        var body = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        body["domainJoined"]!.GetValue<bool>().Should().BeTrue();
        body["domainName"]!.GetValue<string>().Should().Be("CORP");
        body["windowsUser"]!.GetValue<string>().Should().Be("CORP\\alice");
    }

    [Fact]
    public async Task RegisterDeviceAsync_posture_overload_signs_when_device_secret_is_configured()
    {
        var handler = new CapturingHandler(RegisterDeviceResponseJson);
        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.test")
        });
        var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "device-secret");

        await client.RegisterDeviceAsync(
            identity,
            "WIN-001",
            "Windows 11",
            "0.1.0",
            new AgentDevicePosture(true, "CORP", "CORP\\alice"),
            CancellationToken.None);

        var body = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        var signature = body["deviceSignature"]!.AsObject();
        signature["nonce"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        signature["signatureBase64"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    private static string RegisterDeviceResponseJson { get; } =
        """
        {
          "tenantId": "00000000-0000-0000-0000-000000000001",
          "userId": "00000000-0000-0000-0000-000000000002",
          "deviceId": "00000000-0000-0000-0000-000000000003",
          "hostname": "WIN-001",
          "operatingSystem": "Windows 11",
          "agentVersion": "0.1.0",
          "status": "registered",
          "registeredAtUtc": "2026-01-01T00:00:00Z",
          "lastHeartbeatAtUtc": null
        }
        """;

    private static void AssertNullOrMissing(JsonObject body, string propertyName)
    {
        if (body.TryGetPropertyValue(propertyName, out var value))
        {
            value.Should().BeNull();
        }
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class RecordingServerClient : IDrmServerClient
    {
        public AgentIdentity? RegisteredIdentity { get; private set; }

        public AgentDevicePosture? RegisteredPosture { get; private set; }

        public int RegisterDevicePostureOverloadCallCount { get; private set; }

        public (AgentIdentity Identity, string Status, string AgentVersion)? RecordedHeartbeat { get; private set; }

        public AgentDevicePosture? RecordedHeartbeatPosture { get; private set; }

        public int RecordHeartbeatPostureOverloadCallCount { get; private set; }

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

        public Task<AgentDeviceRegistration> RegisterDeviceAsync(
            AgentIdentity identity,
            string hostname,
            string operatingSystem,
            string agentVersion,
            AgentDevicePosture posture,
            CancellationToken cancellationToken)
        {
            RegisteredIdentity = identity;
            RegisteredPosture = posture;
            RegisterDevicePostureOverloadCallCount++;

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

        public Task<AgentHeartbeat> RecordHeartbeatAsync(
            AgentIdentity identity,
            string status,
            string agentVersion,
            AgentDevicePosture posture,
            CancellationToken cancellationToken)
        {
            RecordedHeartbeat = (identity, status, agentVersion);
            RecordedHeartbeatPosture = posture;
            RecordHeartbeatPostureOverloadCallCount++;
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

        public Task WrapFileKeyAsync(Guid tenantId, Guid fileId, byte[] fileKey, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<UnwrappedFileKey> UnwrapFileKeyAsync(Guid tenantId, Guid fileId, Guid userId, Guid deviceId, string requestedPermission, CancellationToken cancellationToken)
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
