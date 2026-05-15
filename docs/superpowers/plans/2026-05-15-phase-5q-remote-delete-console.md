# Phase 5Q Remote Delete Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and superpowers:test-driven-development to implement this plan task-by-task.

**Goal:** Expose the existing remote protected-copy delete command queue in the `/admin/` console.

**Architecture:** Reuse the current admin endpoint `POST /api/admin/files/{fileId}/commands/delete-protected-copy`. The console adds a small form in the Protected files section that accepts a file ID and target device ID, sends tenant/admin identity in the request body, and reports that the command was queued. The endpoint remains responsible for verifying the file and device exist in the tenant; the endpoint agent remains responsible for safe local deletion only from protected-file inventory.

## Tasks

### Task 1: Static Console Tests

- [x] **Step 1: Add failing static expectations**

Update `ManagementConsoleTests` to assert the index contains:

- `Delete protected copy`
- `deleteCopyForm`
- `Target device ID`

and the JavaScript asset contains:

- `/commands/delete-protected-copy`
- `deleteProtectedCopy`
- `deleteCopyForm`

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "ManagementConsoleTests"
```

Expected: FAIL because the console does not yet expose remote delete commands.

### Task 2: Console Implementation

- [x] **Step 1: Add protected-copy delete form**

Add a form to the Protected files panel with file ID, target device ID, and a danger-styled `Delete protected copy` submit button.

- [x] **Step 2: Wire JavaScript command enqueue**

Add `deleteProtectedCopy()` that posts `{ tenantId, deviceId, adminUserId }` to `/api/admin/files/{fileId}/commands/delete-protected-copy`.

- [x] **Step 3: Run passing focused tests**

Run the same filtered command. Expected: PASS.

### Task 3: Documentation and Verification

- [x] **Step 1: Update README**

Document Phase 5Q.

- [x] **Step 2: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

- [x] **Step 3: Commit**

Commit as:

```bash
git commit -m "feat: expose remote delete commands"
```
