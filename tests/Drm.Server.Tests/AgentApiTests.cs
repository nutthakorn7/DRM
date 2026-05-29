using System.Net;
using System.Net.Http.Json;
using Drm.Domain;
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
    public async Task Agent_registration_rejects_disabled_existing_device()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        using var createResponse = await RegisterDeviceAsync(client, tenantId, userId, deviceId, "WIN-001");
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await DisableDeviceAsync(client, tenantId, deviceId);

        using var updateResponse = await client.PostAsJsonAsync("/api/agent/devices/register", new
        {
            tenantId,
            userId,
            deviceId,
            hostname = "WIN-RENAMED",
            operatingSystem = "Windows 11 24H2",
            agentVersion = "0.1.1"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await updateResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new { ReasonCode = "device_disabled" });
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
    public async Task Heartbeat_for_disabled_device_returns_forbidden_and_does_not_reenable_device()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        using var registerResponse = await RegisterDeviceAsync(client, tenantId, userId, deviceId, "WIN-001");
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await DisableDeviceAsync(client, tenantId, deviceId);

        using var heartbeatResponse = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.1.1"
        });

        heartbeatResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await heartbeatResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new { ReasonCode = "device_disabled" });

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await dbContext.AgentDevices.AsNoTracking().SingleAsync();
        device.Status.Should().Be("disabled");
        device.LastHeartbeatAtUtc.Should().BeNull();
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
    public async Task Registration_with_posture_for_unprovisioned_device_requires_device_signature()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/agent/devices/register", new
        {
            tenantId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            hostname = "WIN-001",
            operatingSystem = "Windows 11",
            agentVersion = "0.1.0",
            domainJoined = true,
            domainName = "CORP",
            windowsUser = "CORP\\alice"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new { ReasonCode = "device_signature_required" });
    }

    [Fact]
    public async Task Heartbeat_with_posture_requires_matching_device_signature()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var deviceSecret = DeviceRequestSigning.GenerateDeviceSecret();

        await SeedProvisionedDeviceAsync(tenantId, userId, deviceId, deviceSecret);

        using var unsigned = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.1.1",
            domainJoined = true,
            domainName = "CORP",
            windowsUser = "CORP\\alice"
        });

        unsigned.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unsignedError = await unsigned.Content.ReadFromJsonAsync<ErrorResponse>();
        unsignedError.Should().BeEquivalentTo(new { ReasonCode = "device_signature_required" });

        var signature = DeviceRequestSigning.Sign(
            deviceSecret,
            DeviceRequestSigning.HeartbeatPayload(
                tenantId,
                userId,
                deviceId,
                "online",
                "0.1.1",
                true,
                "CORP",
                "CORP\\alice"));

        using var signed = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.1.1",
            domainJoined = true,
            domainName = "CORP",
            windowsUser = "CORP\\alice",
            deviceSignature = signature
        });

        signed.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await dbContext.AgentDevices.AsNoTracking().SingleAsync();
        device.DomainJoined.Should().BeTrue();
        device.DomainName.Should().Be("CORP");
        device.WindowsUser.Should().Be("CORP\\alice");
    }

    [Fact]
    public async Task Provisioned_device_rejects_replayed_signed_heartbeat()
    {
        // PR #50 hardening: a captured, valid signed heartbeat must not be
        // replayable within the clock-skew window.
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var deviceSecret = DeviceRequestSigning.GenerateDeviceSecret();

        await SeedProvisionedDeviceAsync(tenantId, userId, deviceId, deviceSecret);

        var signature = DeviceRequestSigning.Sign(
            deviceSecret,
            DeviceRequestSigning.HeartbeatPayload(
                tenantId, userId, deviceId, "online", "0.1.1", true, "CORP", "CORP\\alice"));

        object Body() => new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.1.1",
            domainJoined = true,
            domainName = "CORP",
            windowsUser = "CORP\\alice",
            deviceSignature = signature
        };

        using var first = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", Body());
        first.StatusCode.Should().Be(HttpStatusCode.OK, "the first signed heartbeat is fresh");

        using var second = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", Body());
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new { ReasonCode = "device_signature_replayed" });
    }

    [Fact]
    public async Task Provisioned_device_rejects_unsigned_heartbeat_even_without_posture()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var deviceSecret = DeviceRequestSigning.GenerateDeviceSecret();

        await SeedProvisionedDeviceAsync(tenantId, userId, deviceId, deviceSecret);

        using var response = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.1.1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new { ReasonCode = "device_signature_required" });
    }

    [Fact]
    public async Task Provisioned_device_rejects_signed_heartbeat_for_different_user()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var provisionedUserId = Guid.NewGuid();
        var requestUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var deviceSecret = DeviceRequestSigning.GenerateDeviceSecret();

        await SeedProvisionedDeviceAsync(tenantId, provisionedUserId, deviceId, deviceSecret);

        var signature = DeviceRequestSigning.Sign(
            deviceSecret,
            DeviceRequestSigning.HeartbeatPayload(
                tenantId,
                requestUserId,
                deviceId,
                "online",
                "0.1.1",
                null,
                null,
                null));

        using var response = await client.PostAsJsonAsync($"/api/agent/devices/{deviceId}/heartbeat", new
        {
            tenantId,
            userId = requestUserId,
            status = "online",
            agentVersion = "0.1.1",
            deviceSignature = signature
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().BeEquivalentTo(new { ReasonCode = "device_user_mismatch" });
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

    [Fact]
    public async Task Admin_can_provision_device_secret_once_for_msi_deployment()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync("/api/admin/devices/provision", new
        {
            tenantId,
            userId,
            deviceId,
            hostname = "WIN-001",
            operatingSystem = "Windows 11",
            agentVersion = "1.0.0"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProvisionDeviceResponse>();
        body!.DeviceId.Should().Be(deviceId);
        body.DeviceSecret.Should().NotBeNullOrWhiteSpace();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await dbContext.AgentDevices.AsNoTracking().SingleAsync();
        device.DeviceSigningKeyHashBase64.Should()
            .Be(DeviceRequestSigning.DeriveSigningKeyHashBase64(body.DeviceSecret));
        device.Status.Should().Be("provisioned");
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

    private static async Task DisableDeviceAsync(HttpClient client, Guid tenantId, Guid deviceId)
    {
        using var response = await client.PostAsJsonAsync($"/api/admin/devices/{deviceId}/disable", new
        {
            tenantId,
            adminUserId = Guid.NewGuid(),
            reason = "admin_disabled"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task SeedProvisionedDeviceAsync(
        Guid tenantId,
        Guid userId,
        Guid deviceId,
        string deviceSecret)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.AgentDevices.Add(new AgentDeviceEntity
        {
            TenantId = tenantId,
            UserId = userId,
            DeviceId = deviceId,
            Hostname = "WIN-001",
            OperatingSystem = "Windows 11",
            AgentVersion = "0.1.0",
            Status = "provisioned",
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            DeviceSigningKeyHashBase64 = DeviceRequestSigning.DeriveSigningKeyHashBase64(deviceSecret),
            DeviceSigningKeyUpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
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

    private sealed record ErrorResponse(string ReasonCode);

    private sealed record ProvisionDeviceResponse(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string DeviceSecret,
        DateTimeOffset ProvisionedAtUtc);
}
