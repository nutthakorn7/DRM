# Phase 4E Admin File Revocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add admin-scoped file revocation and expose it in the management console.

**Architecture:** Keep the existing `/api/files/{fileId}/revoke` compatibility endpoint, and add `/api/admin/files/{fileId}/revoke` for management operations. File list responses include `revoked` so the console can show state and disable repeat actions. Admin revoke writes audit/SIEM events and policy denies revoked files through existing policy evaluation.

**Tech Stack:** .NET 10 minimal APIs, EF Core, vanilla JS console, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs`: add admin revoke route and include `Revoked` in file list response.
- Modify `tests/Drm.Server.Tests/AdminFilesApiTests.cs`: add tests for admin revoke, wrong tenant, and revoked list state.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add admin user ID input and revoke action column.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: send revoke requests and render revoked status.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add quiet status badges and danger button style.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert revoke UI/API references.
- Modify `README.md`: document admin revocation.

## Tasks

### Task 1: Admin Revoke API

- [x] **Step 1: Write failing API tests**

Add tests that assert:
- `POST /api/admin/files/{fileId}/revoke` with `{ tenantId, adminUserId }` returns 200, sets `revoked = true` in admin file list, and policy decision returns `reasonCode = revoked`.
- Wrong tenant returns 404 and does not revoke the actual file.

- [x] **Step 2: Run failing API tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminFilesApiTests
```

Expected: FAIL because the admin revoke route and `revoked` file list field do not exist.

- [x] **Step 3: Implement admin revoke**

Add `group.MapPost("/{fileId:guid}/revoke", RevokeFileAsync)` to `AdminFilesEndpoints`, update `FileResponse` with `bool Revoked`, and audit `file_revoked`.

- [x] **Step 4: Run passing API tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminFilesApiTests
```

Expected: PASS.

### Task 2: Console Revoke Action

- [x] **Step 1: Write failing console tests**

Extend `ManagementConsoleTests` to assert HTML/JS include:
- `Admin user ID`
- `Revoke`
- `/revoke`
- `adminUserId`
- `revoked`

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL until console assets are updated.

- [x] **Step 3: Implement console revoke**

Add admin user ID session input, render revoked status in file table, and add revoke buttons that call `POST /api/admin/files/{fileId}/revoke`.

- [x] **Step 4: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Document admin revoke and console file status.

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-4e-admin-file-revocation.md
git commit -m "feat: add admin file revocation"
```

## Self-Review

- Spec coverage: Adds explicit management revocation and console controls for revoked state.
- Security note: Admin revoke remains protected by `X-DRM-Admin-Key`.
- Placeholder scan: No TBD/TODO placeholders.
