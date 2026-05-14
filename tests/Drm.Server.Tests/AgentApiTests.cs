using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AgentApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-agent-api-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AgentApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Agent_can_register_device_and_registration_is_audited()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync("/api/agent/devices/register", new
        {
            tenantId,
            userId,
            deviceId,
            hostname = "WIN-001",
            operatingSystem = "Windows 11",
            agentVersion = "0.1.0"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var registered = await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>();
        registered.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            UserId = userId,
            DeviceId = deviceId,
            Hostname = "WIN-001",
            OperatingSystem = "Windows 11",
            AgentVersion = "0.1.0",
            Status = "registered"
        });
        registered!.RegisteredAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        registered.LastHeartbeatAtUtc.Should().BeNull();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await dbContext.AgentDevices.AsNoTracking().SingleAsync();
        device.TenantId.Should().Be(tenantId);
        device.UserId.Should().Be(userId);
        device.DeviceId.Should().Be(deviceId);
        device.Hostname.Should().Be("WIN-001");

        var audit = await dbContext.AuditEvents.AsNoTracking().SingleAsync();
        audit.TenantId.Should().Be(tenantId);
        audit.UserId.Should().Be(userId);
        audit.EventType.Should().Be("agent_registered");
        audit.ReasonCode.Should().Be("registered");
    }

    [Fact]
    public async Task Agent_registration_updates_existing_device_metadata()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        using var createResponse = await RegisterDeviceAsync(client, tenantId, userId, deviceId, "WIN-001");
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var updateResponse = await client.PostAsJsonAsync("/api/agent/devices/register", new
        {
            tenantId,
            userId,
            deviceId,
            hostname = "WIN-RENAMED",
            operatingSystem = "Windows 11 24H2",
            agentVersion = "0.1.1"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await dbContext.AgentDevices.AsNoTracking().SingleAsync();
        device.Hostname.Should().Be("WIN-RENAMED");
        device.OperatingSystem.Should().Be("Windows 11 24H2");
        device.AgentVersion.Should().Be("0.1.1");
    }

    [Fact]
    public async Task Agent_can_send_heartbeat_for_registered_device()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        using var registerResponse = await RegisterDeviceAsync(client, tenantId, userId, deviceId, "WIN-001");
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var heartbeatResponse = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.1.1"
        });

        heartbeatResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var heartbeat = await heartbeatResponse.Content.ReadFromJsonAsync<HeartbeatResponse>();
        heartbeat.Should().NotBeNull();
        heartbeat!.DeviceId.Should().Be(deviceId);
        heartbeat.Status.Should().Be("online");
        heartbeat.LastHeartbeatAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await dbContext.AgentDevices.AsNoTracking().SingleAsync();
        device.Status.Should().Be("online");
        device.AgentVersion.Should().Be("0.1.1");
        device.LastHeartbeatAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var auditEvents = await dbContext.AuditEvents.AsNoTracking().OrderBy(audit => audit.Id).ToListAsync();
        auditEvents.Should().Contain(audit => audit.EventType == "agent_heartbeat" && audit.ReasonCode == "online");
    }

    [Fact]
    public async Task Heartbeat_for_unknown_device_returns_not_found()
    {
        using var client = factory.CreateClient();

        using var heartbeatResponse = await client.PostAsJsonAsync($"/api/agent/devices/{Guid.NewGuid()}/heartbeat", new
        {
            tenantId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            status = "online",
            agentVersion = "0.1.0"
        });

        heartbeatResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Agent_can_upload_audit_event()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync("/api/agent/audit", new
        {
            tenantId,
            userId,
            deviceId,
            fileId,
            eventType = "access_allowed",
            reasonCode = "allowed",
            createdAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5)
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await dbContext.AuditEvents.AsNoTracking().SingleAsync();
        audit.TenantId.Should().Be(tenantId);
        audit.UserId.Should().Be(userId);
        audit.FileId.Should().Be(fileId);
        audit.EventType.Should().Be("access_allowed");
        audit.ReasonCode.Should().Be("allowed");
    }

    [Fact]
    public async Task Agent_audit_rejects_unapproved_event_type()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/agent/audit", new
        {
            tenantId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            fileId = (Guid?)null,
            eventType = "admin_changed",
            reasonCode = "spoofed",
            createdAtUtc = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static Task<HttpResponseMessage> RegisterDeviceAsync(
        HttpClient client,
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        string hostname)
    {
        return client.PostAsJsonAsync("/api/agent/devices/register", new
        {
            tenantId,
            userId,
            deviceId,
            hostname,
            operatingSystem = "Windows 11",
            agentVersion = "0.1.0"
        });
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record RegisterDeviceResponse(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string Hostname,
        string OperatingSystem,
        string AgentVersion,
        string Status,
        DateTimeOffset RegisteredAtUtc,
        DateTimeOffset? LastHeartbeatAtUtc);

    private sealed record HeartbeatResponse(Guid DeviceId, string Status, DateTimeOffset LastHeartbeatAtUtc);
}
