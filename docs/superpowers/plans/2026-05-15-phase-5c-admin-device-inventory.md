# Phase 5C Admin Device Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let administrators list registered desktop agent devices from the management API and console.

**Architecture:** Add a read-only `/api/admin/devices` endpoint over existing `AgentDevices`. The endpoint is tenant-scoped, optionally filters by `userId` and `status`, and returns device metadata/heartbeat timestamps. Add a management console Devices section using the existing admin API key flow.

**Tech Stack:** .NET 10 minimal APIs, EF Core, vanilla JS console, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Server/Endpoints/AdminDevicesEndpoints.cs`: list devices endpoint.
- Modify `src/Drm.Server/Program.cs`: map admin device endpoint.
- Create `tests/Drm.Server.Tests/AdminDevicesApiTests.cs`: tenant scoping/filtering tests.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert Devices UI/API references.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add Devices section.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: fetch/render devices.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add device filter layout.
- Modify `README.md`: document admin device inventory.

## Tasks

### Task 1: Admin Devices API

- [x] **Step 1: Write failing API test**

Create `tests/Drm.Server.Tests/AdminDevicesApiTests.cs` with a test that registers devices through `/api/agent/devices/register`, updates heartbeat for one device, then calls:

```text
GET /api/admin/devices?tenantId={tenantId}&status=online&userId={userId}
```

Assert only the matching tenant/user/status device is returned and the response includes hostname, OS, agent version, status, registered, updated, and last heartbeat timestamps.

- [x] **Step 2: Run failing API test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminDevicesApiTests
```

Expected: FAIL because `/api/admin/devices` does not exist.

- [x] **Step 3: Implement admin devices endpoint**

Add `AdminDevicesEndpoints`:

```csharp
public static IEndpointRouteBuilder MapAdminDevicesEndpoints(this IEndpointRouteBuilder endpoints)
{
    var group = endpoints.MapGroup("/api/admin/devices");
    group.MapGet("/", ListDevicesAsync);
    return endpoints;
}
```

`ListDevicesAsync(Guid tenantId, Guid? userId, string? status, AppDbContext dbContext, CancellationToken cancellationToken)` filters by tenant, optional user/status, orders by hostname then device ID, takes 500, and returns `DeviceResponse`.

- [x] **Step 4: Run passing API test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminDevicesApiTests
```

Expected: PASS.

### Task 2: Console Devices UI

- [x] **Step 1: Write failing console asset tests**

Update `ManagementConsoleTests` to assert HTML contains:

```csharp
html.Should().Contain("Agent devices");
html.Should().Contain("Filter by status");
html.Should().Contain("Filter by user ID");
```

Assert JS contains:

```csharp
js.Should().Contain("/api/admin/devices");
js.Should().Contain("refreshDevices");
js.Should().Contain("devicesBody");
```

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL until console assets are updated.

- [x] **Step 3: Implement console devices section**

Add a `Devices` nav link and `#devices` section with status/user filters, refresh button, and table columns: Hostname, Device ID, User ID, OS, Version, Status, Last heartbeat.

Add JS `refreshDevices()` and `renderDevices(devices)` using `/api/admin/devices?tenantId=...&status=...&userId=...`.

- [x] **Step 4: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Add Phase 5C note for `/api/admin/devices` and console device inventory.

- [x] **Step 2: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 3: Live smoke**

Run a temp server, register a device, send a heartbeat, and verify `/api/admin/devices` returns it with admin key.

- [x] **Step 4: Commit**

Run:

```bash
git add README.md src/Drm.Server tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5c-admin-device-inventory.md
git commit -m "feat: add admin device inventory"
```

## Self-Review

- Spec coverage: Adds tenant-scoped device visibility for desktop agents.
- Security note: Endpoint lives under `/api/admin/*` and is protected by admin API key when configured.
- Placeholder scan: No TBD/TODO placeholders.
