using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AdminDevicesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-devices-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminDevicesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_list_devices_is_tenant_scoped_and_filters_by_user_and_status()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var onlineDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var registeredDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var otherTenantDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        using var onlineRegister = await RegisterDeviceAsync(client, tenantId, userId, onlineDeviceId, "WIN-ONLINE");
        using var registeredRegister = await RegisterDeviceAsync(client, tenantId, otherUserId, registeredDeviceId, "WIN-REGISTERED");
        using var otherTenantRegister = await RegisterDeviceAsync(client, otherTenantId, userId, otherTenantDeviceId, "WIN-OTHER");
        onlineRegister.StatusCode.Should().Be(HttpStatusCode.Created);
        registeredRegister.StatusCode.Should().Be(HttpStatusCode.Created);
        otherTenantRegister.StatusCode.Should().Be(HttpStatusCode.Created);

        using var heartbeat = await client.PostAsJsonAsync($"/api/agent/devices/{onlineDeviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.2.0"
        });
        heartbeat.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await client.GetAsync(
            $"/api/admin/devices?tenantId={tenantId}&userId={userId}&status=online");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var devices = await response.Content.ReadFromJsonAsync<List<DeviceResponse>>();
        devices.Should().ContainSingle();
        devices![0].Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            DeviceId = onlineDeviceId,
            UserId = userId,
            Hostname = "WIN-ONLINE",
            OperatingSystem = "Windows 11",
            AgentVersion = "0.2.0",
            Status = "online"
        });
        devices[0].RegisteredAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        devices[0].UpdatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        devices[0].LastHeartbeatAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Admin_can_disable_device_and_disable_is_tenant_scoped_and_audited()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        using var register = await RegisterDeviceAsync(client, tenantId, userId, deviceId, "WIN-LOST");
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        using var wrongTenantDisable = await client.PostAsJsonAsync($"/api/admin/devices/{deviceId}/disable", new
        {
            tenantId = otherTenantId,
            adminUserId,
            reason = "lost_device"
        });
        wrongTenantDisable.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var disable = await client.PostAsJsonAsync($"/api/admin/devices/{deviceId}/disable", new
        {
            tenantId,
            adminUserId,
            reason = " lost_device "
        });

        disable.StatusCode.Should().Be(HttpStatusCode.OK);
        var disabled = await disable.Content.ReadFromJsonAsync<DeviceResponse>();
        disabled.Should().NotBeNull();
        disabled!.TenantId.Should().Be(tenantId);
        disabled.DeviceId.Should().Be(deviceId);
        disabled.UserId.Should().Be(userId);
        disabled.Status.Should().Be("disabled");
        disabled.DisabledReason.Should().Be("lost_device");
        disabled.DisabledAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        disabled.UpdatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var device = await dbContext.AgentDevices.AsNoTracking().SingleAsync();
        device.Status.Should().Be("disabled");
        device.DisabledReason.Should().Be("lost_device");
        device.DisabledAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var audit = await dbContext.AuditEvents.AsNoTracking()
            .SingleAsync(auditEvent => auditEvent.EventType == "device_disabled");
        audit.TenantId.Should().Be(tenantId);
        audit.UserId.Should().Be(adminUserId);
        audit.ReasonCode.Should().Be("lost_device");
    }

    [Fact]
    public async Task Admin_device_health_summarizes_online_stale_never_seen_and_disabled_devices()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var onlineDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var staleDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var neverSeenDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000103");
        var disabledDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000104");
        var otherTenantDeviceId = Guid.Parse("00000000-0000-0000-0000-000000000105");

        await RegisterDeviceAsync(client, tenantId, userId, onlineDeviceId, "WIN-ONLINE");
        await RegisterDeviceAsync(client, tenantId, userId, staleDeviceId, "WIN-STALE");
        await RegisterDeviceAsync(client, tenantId, userId, neverSeenDeviceId, "WIN-NEW");
        await RegisterDeviceAsync(client, tenantId, userId, disabledDeviceId, "WIN-DISABLED");
        await RegisterDeviceAsync(client, otherTenantId, userId, otherTenantDeviceId, "WIN-OTHER");

        using var onlineHeartbeat = await client.PostAsJsonAsync($"/api/agent/devices/{onlineDeviceId}/heartbeat", new
        {
            tenantId,
            userId,
            status = "online",
            agentVersion = "0.2.0"
        });
        onlineHeartbeat.StatusCode.Should().Be(HttpStatusCode.OK);

        using var disable = await client.PostAsJsonAsync($"/api/admin/devices/{disabledDeviceId}/disable", new
        {
            tenantId,
            adminUserId = Guid.NewGuid(),
            reason = "lost_device"
        });
        disable.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stale = await dbContext.AgentDevices.SingleAsync(device => device.DeviceId == staleDeviceId);
            stale.LastHeartbeatAtUtc = DateTimeOffset.UtcNow.AddHours(-2);
            stale.Status = "online";
            await dbContext.SaveChangesAsync();
        }

        using var response = await client.GetAsync($"/api/admin/devices/health?tenantId={tenantId}&staleAfterMinutes=30");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var health = await response.Content.ReadFromJsonAsync<DeviceHealthResponse>();
        health.Should().BeEquivalentTo(new
        {
            TenantId = tenantId,
            Total = 4,
            Online = 1,
            Stale = 1,
            NeverSeen = 1,
            Disabled = 1,
            StaleAfterMinutes = 30
        });
        health!.StaleThresholdUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(1));
        health.NewestHeartbeatAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Admin_device_health_rejects_invalid_stale_threshold()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/admin/devices/health?tenantId={Guid.NewGuid()}&staleAfterMinutes=0");

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

    private sealed record DeviceResponse(
        Guid TenantId,
        Guid DeviceId,
        Guid UserId,
        string Hostname,
        string OperatingSystem,
        string AgentVersion,
        string Status,
        DateTimeOffset RegisteredAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? LastHeartbeatAtUtc,
        DateTimeOffset? DisabledAtUtc,
        string? DisabledReason);

    private sealed record DeviceHealthResponse(
        Guid TenantId,
        int Total,
        int Online,
        int Stale,
        int NeverSeen,
        int Disabled,
        int StaleAfterMinutes,
        DateTimeOffset StaleThresholdUtc,
        DateTimeOffset? NewestHeartbeatAtUtc);
}
