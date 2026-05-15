# Phase 5D Device Disable Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let administrators disable a registered desktop device and make the server deny future device heartbeats, policy decisions, and file-key unwraps for that device.

**Architecture:** Extend `AgentDeviceEntity` with disable metadata and expose `POST /api/admin/devices/{deviceId}/disable`. Device disable is tenant-scoped, records an audit event, returns the updated device, prevents disabled devices from heartbeating back online, and makes `PolicyDecisionService` deny access before evaluating file grants.

**Tech Stack:** .NET 10 minimal APIs, EF Core, vanilla JS admin console, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Entities.cs`: add `DisabledAtUtc` and `DisabledReason` to `AgentDeviceEntity`.
- Modify `src/Drm.Server/AppDbContext.cs`: configure disable reason length.
- Modify `src/Drm.Server/Endpoints/AdminDevicesEndpoints.cs`: add disable endpoint and response metadata.
- Modify `src/Drm.Server/Endpoints/AgentEndpoints.cs`: reject register/heartbeat attempts for disabled devices.
- Modify `src/Drm.Server/PolicyDecisionService.cs`: deny policy decisions for disabled devices.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add Disable column to the Devices table.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: render device disable actions.
- Modify `tests/Drm.Server.Tests/AdminDevicesApiTests.cs`: cover disable endpoint.
- Modify `tests/Drm.Server.Tests/AgentApiTests.cs`: cover disabled heartbeat/register rejection.
- Modify `tests/Drm.Server.Tests/PolicyApiTests.cs`: cover `device_disabled` policy decision.
- Modify `tests/Drm.Server.Tests/FileKeyApiTests.cs`: cover disabled device unwrap denial.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert console disable references.
- Modify `README.md`: document device disable enforcement.

## Tasks

### Task 1: Admin Device Disable API

- [x] **Step 1: Write failing admin API test**

Add a test to `tests/Drm.Server.Tests/AdminDevicesApiTests.cs` that registers a device, calls:

```text
POST /api/admin/devices/{deviceId}/disable
```

with `tenantId`, `adminUserId`, and `reason`, then asserts status 200, `status = "disabled"`, `disabledAtUtc` is set, `disabledReason = "lost_device"`, and an audit event `device_disabled/lost_device` exists. Also assert a different tenant cannot disable the device and gets 404.

- [x] **Step 2: Run failing admin API test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminDevicesApiTests
```

Expected: FAIL because the endpoint and fields do not exist.

- [x] **Step 3: Implement disable endpoint**

Add disable metadata to the device entity, map `POST /api/admin/devices/{deviceId:guid}/disable`, update the matching tenant/device to `Status = "disabled"`, store trimmed reason, set `DisabledAtUtc`, update `UpdatedAtUtc`, and add an admin audit event.

- [x] **Step 4: Run passing admin API test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminDevicesApiTests
```

Expected: PASS.

### Task 2: Enforcement

- [x] **Step 1: Write failing enforcement tests**

Add tests that prove:

- `AgentApiTests`: heartbeat for a disabled device returns 403 with `reasonCode = "device_disabled"` and leaves `Status` disabled.
- `AgentApiTests`: re-registering a disabled device returns 403 with `reasonCode = "device_disabled"`.
- `PolicyApiTests`: `/api/policy/decide` for a disabled registered device returns 200 with `allowed = false`, `reasonCode = "device_disabled"`, and no offline lease.
- `FileKeyApiTests`: `/api/files/{fileId}/keys/unwrap` for a disabled device returns 403.

- [x] **Step 2: Run failing enforcement tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "AgentApiTests|PolicyApiTests|FileKeyApiTests"
```

Expected: FAIL on the new disabled-device cases.

- [x] **Step 3: Implement enforcement**

In agent registration and heartbeat, return forbidden when an existing device has `DisabledAtUtc` set. In `PolicyDecisionService`, after confirming a valid permission and before loading grants, check for a tenant/device row with `DisabledAtUtc != null`; add `access_denied/device_disabled` audit and return denied.

- [x] **Step 4: Run passing enforcement tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "AgentApiTests|PolicyApiTests|FileKeyApiTests"
```

Expected: PASS.

### Task 3: Console, Docs, Verification

- [x] **Step 1: Write failing console asset tests**

Update `ManagementConsoleTests` to assert HTML contains `Disable` and JS contains `disableDevice`.

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL until console assets are updated.

- [x] **Step 3: Implement console disable action**

Add a Disable column to the Devices table. Render a Disable button only for non-disabled devices. `disableDevice(deviceId)` posts `{ tenantId, adminUserId, reason: "admin_disabled" }` to `/api/admin/devices/{deviceId}/disable`, then refreshes devices.

- [x] **Step 4: Update README**

Add Phase 5D notes for `/api/admin/devices/{deviceId}/disable`, disabled heartbeat behavior, and `device_disabled` policy/key denial.

- [x] **Step 5: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 6: Live smoke**

Run a temp server, register a device, disable it through the admin API, verify heartbeat returns 403, verify policy decision returns `device_disabled`, and use Playwright CLI to confirm the Devices table has a Disable action with no console errors.

- [x] **Step 7: Commit**

Run:

```bash
git add README.md src/Drm.Server tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5d-device-disable-enforcement.md
git commit -m "feat: enforce disabled devices"
```

## Self-Review

- Spec coverage: Implements device trust state management from the approved enterprise DRM design.
- Security note: Disable is admin-only, tenant-scoped, auditable, and denies future key release for the device.
- Placeholder scan: No TBD/TODO placeholders.
