# Phase 5G Admin Policy Simulator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let administrators preview whether a user/device/action would be allowed for a protected file without issuing a file key or writing access audit events.

**Architecture:** Refactor `PolicyDecisionService` so regular `/api/policy/decide` keeps auditing while a new admin-only `/api/admin/policy-simulator` endpoint uses the same decision logic with audit disabled. Add a management console simulator section that posts tenant/file/user/device/permission inputs and renders the decision.

**Tech Stack:** .NET 10 minimal APIs, EF Core, vanilla JS admin console, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/PolicyDecisionService.cs`: add `SimulateAsync` and shared private decision flow with optional audit.
- Create `src/Drm.Server/Endpoints/AdminPolicySimulatorEndpoints.cs`: admin simulator endpoint.
- Modify `src/Drm.Server/Program.cs`: map simulator endpoint.
- Create `tests/Drm.Server.Tests/AdminPolicySimulatorApiTests.cs`: response and no-audit coverage.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert simulator UI/API references.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add Policy simulator section.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: call simulator endpoint and render decision.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add simulator form/output layout.
- Modify `README.md`: document Phase 5G simulator behavior.

## Tasks

### Task 1: Simulator API

- [x] **Step 1: Write failing API tests**

Create `tests/Drm.Server.Tests/AdminPolicySimulatorApiTests.cs`:

- Register a protected file with `View` permission.
- Record current audit count.
- POST `/api/admin/policy-simulator` with tenant, file, owner user, device, and `requestedPermission = "View"`.
- Assert 200 with `allowed = true`, `allowedPermissions = "View"`, `reasonCode = "allowed"`, `simulated = true`.
- Assert audit count did not increase.
- Add a second test that invalid `requestedPermission = "Fly"` returns 400.

- [x] **Step 2: Run failing API tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminPolicySimulatorApiTests
```

Expected: FAIL because `/api/admin/policy-simulator` does not exist.

- [x] **Step 3: Implement no-audit simulator endpoint**

Add `PolicyDecisionService.SimulateAsync(...)` that reuses decision logic without adding `AuditEvents`. Add `AdminPolicySimulatorEndpoints.MapAdminPolicySimulatorEndpoints()` with:

```text
POST /api/admin/policy-simulator
```

Return 400 for invalid permission, 404 for missing file, and 200 for valid simulations.

- [x] **Step 4: Run passing API tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminPolicySimulatorApiTests
```

Expected: PASS.

### Task 2: Console UI

- [x] **Step 1: Write failing console tests**

Update `ManagementConsoleTests` to assert HTML contains:

- `Policy simulator`
- `Simulate access`

Assert JS contains:

- `/api/admin/policy-simulator`
- `simulatePolicy`
- `simulatorOutput`

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL until console assets are updated.

- [x] **Step 3: Implement simulator UI**

Add nav link `Simulator` and a `#simulator` section with inputs for file ID, user ID, device ID, requested permission, a `Simulate access` button, and a read-only output block. JS posts the request with tenant ID and admin key, then renders allowed/denied, allowed permissions, reason code, watermark, and offline lease.

- [x] **Step 4: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Add Phase 5G note for the admin policy simulator and the no-access-audit behavior.

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

Run a temp server, register a file, call `/api/admin/policy-simulator`, verify `simulated: true`, verify audit count does not increase via admin audit list, then open `/admin/` with Playwright and check no console errors.

- [x] **Step 4: Commit**

Run:

```bash
git add README.md src/Drm.Server tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5g-admin-policy-simulator.md
git commit -m "feat: add admin policy simulator"
```

## Self-Review

- Spec coverage: Adds policy simulator and impact preview groundwork from the approved enterprise DRM design.
- Security note: The simulator is admin-only and does not issue keys or create endpoint access audit events.
- Placeholder scan: No TBD/TODO placeholders.
