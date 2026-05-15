# Phase 4D Management Console Admin Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand the browser management console beyond users so operators can manage groups, group members, protected file lists, and file grants.

**Architecture:** Reuse existing admin APIs from the static console. Add group create/member forms, a protected files table, and a file-grant form. Keep the UI as a dense operational surface with one tenant context and one saved admin API key.

**Tech Stack:** Vanilla HTML/CSS/JS, ASP.NET Core static assets, WebApplicationFactory static asset tests.

---

## File Structure

- Modify `src/Drm.Server/wwwroot/admin/index.html`: add Groups and Files sections.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add responsive multi-section styling and status cells.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: add group create/member flows, files list, and grant upsert flow.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert console assets reference groups/files APIs and UI labels.
- Modify `README.md`: document expanded console operations.

## Tasks

### Task 1: Static Asset Tests

- [x] **Step 1: Write failing tests**

Extend `ManagementConsoleTests` so HTML must contain:
- `Groups`
- `Protected files`
- `Subject type`
- `Permissions`

Extend JS assertions so assets must contain:
- `/api/admin/groups`
- `/api/admin/files`
- `/grants`

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL because the console only has users and health.

### Task 2: Console Operations

- [x] **Step 1: Implement Groups UI**

Add forms for creating a group, adding a user to a group, and listing members by group ID. Use:
- `POST /api/admin/groups`
- `POST /api/admin/groups/{groupId}/members`
- `GET /api/admin/groups/{groupId}/members?tenantId=...`

- [x] **Step 2: Implement Files UI**

Add protected file refresh and grant upsert. Use:
- `GET /api/admin/files?tenantId=...&q=...`
- `POST /api/admin/files/{fileId}/grants`

- [x] **Step 3: Run passing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Document the console now covers users, groups, group members, files, and grants.

- [x] **Step 2: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 3: Commit**

Run:

```bash
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-4d-management-console-admin-operations.md
git commit -m "feat: expand management console operations"
```

## Self-Review

- Spec coverage: Adds core management operations operators need for users, groups, files, and grants.
- Security note: All admin API calls still include `X-DRM-Admin-Key`.
- Placeholder scan: No TBD/TODO placeholders.
