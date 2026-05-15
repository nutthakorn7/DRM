# Phase 5O Agent Health Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a tenant-scoped agent health summary so admins can quickly see online, stale, never-seen, and disabled endpoint counts.

**Architecture:** Extend the existing admin devices endpoint group with `GET /api/admin/devices/health?tenantId=...&staleAfterMinutes=...`. The endpoint computes health from `AgentDevices` only: disabled devices are counted separately, recent heartbeat devices are online, old heartbeat devices are stale, and registered devices with no heartbeat are never seen. The admin console refreshes this summary beside the existing device list.

**Tech Stack:** .NET 10 minimal APIs, EF Core, static admin console HTML/CSS/JS, xUnit, FluentAssertions.

---

## File Structure

- Modify `tests/Drm.Server.Tests/AdminDevicesApiTests.cs`: API tests for device health counts and validation.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: static asset contract for the health summary UI.
- Modify `src/Drm.Server/Endpoints/AdminDevicesEndpoints.cs`: add `GET /api/admin/devices/health`.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add device health summary container.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: fetch and render the health summary.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add compact health summary layout.
- Modify `README.md`: document Phase 5O.

## Tasks

### Task 1: Device Health API

- [x] **Step 1: Write failing API tests**

Add tests to `tests/Drm.Server.Tests/AdminDevicesApiTests.cs`:

```csharp
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
```

Add a local `DeviceHealthResponse` record in the test file matching the endpoint response.

- [x] **Step 2: Run failing API tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Admin_device_health_summarizes_online_stale_never_seen_and_disabled_devices|Admin_device_health_rejects_invalid_stale_threshold"
```

Expected: FAIL because `/api/admin/devices/health` does not exist.

- [x] **Step 3: Implement health endpoint**

Update `src/Drm.Server/Endpoints/AdminDevicesEndpoints.cs`:

- map `group.MapGet("/health", GetDeviceHealthAsync);`
- validate `staleAfterMinutes` in range 1 to 10080, default 15;
- compute:
  - `Total`: all tenant devices;
  - `Disabled`: `DisabledAtUtc != null || Status == "disabled"`;
  - `NeverSeen`: not disabled and `LastHeartbeatAtUtc == null`;
  - `Online`: not disabled and `LastHeartbeatAtUtc >= staleThresholdUtc`;
  - `Stale`: not disabled and `LastHeartbeatAtUtc < staleThresholdUtc`;
  - `NewestHeartbeatAtUtc`: max heartbeat for tenant, nullable.

- [x] **Step 4: Run passing API tests**

Run the same filtered command. Expected: PASS.

### Task 2: Management Console Health Summary

- [x] **Step 1: Write static console expectations**

Update `tests/Drm.Server.Tests/ManagementConsoleTests.cs` to assert:

```csharp
html.Should().Contain("Device health");
html.Should().Contain("deviceHealthSummary");
html.Should().Contain("Stale after");
js.Should().Contain("/api/admin/devices/health");
js.Should().Contain("refreshDeviceHealth");
js.Should().Contain("renderDeviceHealth");
js.Should().Contain("deviceHealthSummary");
```

- [x] **Step 2: Run failing console asset tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "ManagementConsoleTests"
```

Expected: FAIL because the UI summary is not present.

- [x] **Step 3: Add console health UI**

Update the device section:

- add an input `#deviceStaleAfterMinutes` default `15`;
- add a container `#deviceHealthSummary`;
- `refreshDevices()` should call `refreshDeviceHealth()` before or after fetching the list;
- `refreshDeviceHealth()` fetches `/api/admin/devices/health`;
- `renderDeviceHealth()` writes compact metric items for total, online, stale, never seen, disabled, newest heartbeat.

- [x] **Step 4: Run passing console asset tests**

Run the same ManagementConsoleTests command. Expected: PASS.

### Task 3: Documentation and Verification

- [x] **Step 1: Update README**

Add Phase 5O notes for `/api/admin/devices/health` and the console summary.

- [x] **Step 2: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

Expected: all pass.

- [x] **Step 3: Commit**

Run:

```bash
git add README.md src/Drm.Server tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5o-agent-health-dashboard.md
git commit -m "feat: add agent health dashboard"
```

## Self-Review

- Spec coverage: Adds the roadmap's agent health dashboard slice without changing agent heartbeat semantics.
- Security note: This is admin-only summary data and remains protected by the existing admin API key middleware when configured.
- Placeholder scan: No TBD/TODO placeholders.
