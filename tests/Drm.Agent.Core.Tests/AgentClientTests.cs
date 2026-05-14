using System.Net;
using System.Text;
using System.Text.Json;
using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class AgentClientTests
{
    [Fact]
    public async Task DrmServerClient_registers_device_with_agent_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;

            var json = """
                {
                  "tenantId": "4ec64ccb-5f84-4ff5-bcbc-54286b882f36",
                  "userId": "9a5b19a6-229f-4f4a-a4a7-47f01815cf2e",
                  "deviceId": "e1ec77f7-3377-410b-baad-61f6466b1107",
                  "hostname": "WIN-001",
                  "operatingSystem": "Windows 11",
                  "agentVersion": "0.1.0",
                  "status": "registered",
                  "registeredAtUtc": "2026-05-15T00:00:00Z",
                  "lastHeartbeatAtUtc": null
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });
        var identity = new AgentIdentity(
            Guid.Parse("4ec64ccb-5f84-4ff5-bcbc-54286b882f36"),
            Guid.Parse("9a5b19a6-229f-4f4a-a4a7-47f01815cf2e"),
            Guid.Parse("e1ec77f7-3377-410b-baad-61f6466b1107"));

        var registration = await client.RegisterDeviceAsync(
            identity,
            "WIN-001",
            "Windows 11",
            "0.1.0",
            CancellationToken.None);

        registration.Status.Should().Be("registered");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be(new Uri("https://drm.example/api/agent/devices/register"));

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("tenantId").GetGuid().Should().Be(identity.TenantId);
        document.RootElement.GetProperty("userId").GetGuid().Should().Be(identity.UserId);
        document.RootElement.GetProperty("deviceId").GetGuid().Should().Be(identity.DeviceId);
        document.RootElement.GetProperty("hostname").GetString().Should().Be("WIN-001");
    }

    [Fact]
    public async Task DrmServerClient_posts_heartbeat_for_device()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"deviceId":"e1ec77f7-3377-410b-baad-61f6466b1107","status":"online","lastHeartbeatAtUtc":"2026-05-15T00:00:00Z"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });
        var identity = new AgentIdentity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Parse("e1ec77f7-3377-410b-baad-61f6466b1107"));

        var heartbeat = await client.RecordHeartbeatAsync(identity, "online", "0.1.1", CancellationToken.None);

        heartbeat.Status.Should().Be("online");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri.Should().Be(new Uri("https://drm.example/api/agent/devices/e1ec77f7-3377-410b-baad-61f6466b1107/heartbeat"));

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("status").GetString().Should().Be("online");
        document.RootElement.GetProperty("agentVersion").GetString().Should().Be("0.1.1");
    }

    [Fact]
    public async Task DrmServerClient_uploads_agent_audit_record()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });
        var record = new AgentAuditRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "agent_heartbeat",
            "online",
            DateTimeOffset.UtcNow);

        await client.UploadAuditAsync(record, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be(new Uri("https://drm.example/api/agent/audit"));

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("eventType").GetString().Should().Be("agent_heartbeat");
        document.RootElement.GetProperty("reasonCode").GetString().Should().Be("online");
    }

    [Fact]
    public async Task DrmServerClient_gets_pending_agent_commands()
    {
        HttpRequestMessage? capturedRequest = null;
        var commandId = Guid.Parse("32fe8fae-c781-4e35-8dd7-39e656972911");
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            var json = """
                [
                  {
                    "tenantId": "4ec64ccb-5f84-4ff5-bcbc-54286b882f36",
                    "commandId": "COMMAND_ID",
                    "deviceId": "e1ec77f7-3377-410b-baad-61f6466b1107",
                    "fileId": "de470ac0-d8fe-47bb-a1d0-f951a2ef3b2f",
                    "commandType": "DeleteProtectedCopy",
                    "status": "Pending",
                    "reasonCode": "queued",
                    "createdAtUtc": "2026-05-15T01:00:00Z",
                    "completedAtUtc": null
                  }
                ]
                """.Replace("COMMAND_ID", commandId.ToString(), StringComparison.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });
        var identity = new AgentIdentity(
            Guid.Parse("4ec64ccb-5f84-4ff5-bcbc-54286b882f36"),
            Guid.NewGuid(),
            Guid.Parse("e1ec77f7-3377-410b-baad-61f6466b1107"));

        var commands = await client.GetPendingCommandsAsync(identity, CancellationToken.None);

        commands.Should().ContainSingle(command =>
            command.CommandId == commandId &&
            command.CommandType == "DeleteProtectedCopy" &&
            command.Status == "Pending");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri.Should().Be(new Uri("https://drm.example/api/agent/devices/e1ec77f7-3377-410b-baad-61f6466b1107/commands?tenantId=4ec64ccb-5f84-4ff5-bcbc-54286b882f36"));
    }

    [Fact]
    public async Task DrmServerClient_completes_agent_command()
    {
        HttpRequestMessage? capturedRequest = null;
        var commandId = Guid.Parse("32fe8fae-c781-4e35-8dd7-39e656972911");
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "tenantId": "4ec64ccb-5f84-4ff5-bcbc-54286b882f36",
                      "commandId": "{{commandId}}",
                      "deviceId": "e1ec77f7-3377-410b-baad-61f6466b1107",
                      "fileId": "de470ac0-d8fe-47bb-a1d0-f951a2ef3b2f",
                      "commandType": "DeleteProtectedCopy",
                      "status": "Completed",
                      "reasonCode": "deleted",
                      "createdAtUtc": "2026-05-15T01:00:00Z",
                      "completedAtUtc": "2026-05-15T01:01:00Z"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var client = new DrmServerClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://drm.example")
        });
        var identity = new AgentIdentity(
            Guid.Parse("4ec64ccb-5f84-4ff5-bcbc-54286b882f36"),
            Guid.NewGuid(),
            Guid.Parse("e1ec77f7-3377-410b-baad-61f6466b1107"));

        var completed = await client.CompleteCommandAsync(
            identity,
            commandId,
            new AgentCommandCompletion("Completed", "deleted"),
            CancellationToken.None);

        completed.Status.Should().Be("Completed");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri.Should().Be(new Uri("https://drm.example/api/agent/devices/e1ec77f7-3377-410b-baad-61f6466b1107/commands/32fe8fae-c781-4e35-8dd7-39e656972911/complete"));

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("tenantId").GetGuid().Should().Be(identity.TenantId);
        document.RootElement.GetProperty("status").GetString().Should().Be("Completed");
        document.RootElement.GetProperty("reasonCode").GetString().Should().Be("deleted");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}
