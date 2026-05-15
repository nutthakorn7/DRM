# Phase 5S Command Status Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and superpowers:test-driven-development to implement this plan task-by-task.

**Goal:** Let admins see remote delete command status after queueing protected-copy delete commands.

**Architecture:** Add an admin file command listing endpoint under the existing admin files group: `GET /api/admin/files/{fileId}/commands?tenantId=...&deviceId=...`. It returns recent command records for that file, including pending, completed, and failed status. The `/admin/` console adds a compact Command status viewer in the Protected files section.

## Tasks

- [x] **Step 1: Add failing tests**

Add:

- API tests in `AgentCommandApiTests` for tenant-scoped command listing, completed command visibility, and optional device filtering.
- Management console static expectations for command status UI strings and JS functions.

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Admin_can_list_file_commands|ManagementConsoleTests"
```

Expected: FAIL because the admin list endpoint and UI do not exist.

- [x] **Step 3: Implement admin command listing endpoint**

Map `GET /api/admin/files/{fileId}/commands`, check the protected file exists in the tenant, filter by optional device ID, include completed commands, order newest first, and return existing `AgentCommandResponse` shape.

- [x] **Step 4: Implement console status viewer**

Add a Command status form/table in the Protected files panel and JS functions:

- `refreshCommands()`
- `renderCommands()`
- `commandsBody`

- [x] **Step 5: Run focused tests**

Run the same filtered command. Expected: PASS.

- [x] **Step 6: Update README**

Document Phase 5S.

- [x] **Step 7: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

- [x] **Step 8: Commit**

Commit as:

```bash
git commit -m "feat: add command status console"
```
