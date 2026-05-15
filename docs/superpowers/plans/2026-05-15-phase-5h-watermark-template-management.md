# Phase 5H Watermark Template Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let administrators manage reusable watermark patterns separately from policy templates.

**Architecture:** Add a tenant-scoped `WatermarkTemplateEntity` and admin endpoints under `/api/admin/watermark-templates`. The console gets a Watermarks section to create and list patterns; existing policy templates keep their inline watermark text for compatibility.

**Tech Stack:** .NET 10 minimal APIs, EF Core, vanilla JS admin console, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Entities.cs`: add `WatermarkTemplateEntity`.
- Modify `src/Drm.Server/AppDbContext.cs`: add DbSet and EF model configuration.
- Create `src/Drm.Server/Endpoints/AdminWatermarkTemplatesEndpoints.cs`: create/list/get endpoints.
- Modify `src/Drm.Server/Program.cs`: map watermark endpoint.
- Create `tests/Drm.Server.Tests/AdminWatermarkTemplatesApiTests.cs`: endpoint coverage.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert console UI/API references.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add Watermarks section.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: create/list watermark templates.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: add watermark form layout.
- Modify `README.md`: document Phase 5H.

## Tasks

### Task 1: Watermark Template API

- [x] **Step 1: Write failing API tests**

Create `AdminWatermarkTemplatesApiTests` that asserts:

- `POST /api/admin/watermark-templates` creates a template with tenant ID, watermark template ID, name, pattern.
- `GET /api/admin/watermark-templates/{id}?tenantId=...` returns the created template.
- `GET /api/admin/watermark-templates?tenantId=...` lists only that tenant, ordered by name.
- Duplicate template ID in the same tenant returns 409.
- Blank name or blank pattern returns 400.
- Creation emits `system_changed/watermark_template_created`.

- [x] **Step 2: Run failing API tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminWatermarkTemplatesApiTests
```

Expected: FAIL because `/api/admin/watermark-templates` does not exist.

- [x] **Step 3: Implement API**

Add entity/model and endpoint methods for create, list, and get. Validate non-empty IDs, name length <= 256, pattern length <= 1024, conflict on duplicate `(TenantId, WatermarkTemplateId)`, and audit creation.

- [x] **Step 4: Run passing API tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminWatermarkTemplatesApiTests
```

Expected: PASS.

### Task 2: Console UI

- [x] **Step 1: Write failing console tests**

Update `ManagementConsoleTests` to assert HTML contains:

- `Watermark templates`
- `Watermark pattern`

Assert JS contains:

- `/api/admin/watermark-templates`
- `refreshWatermarkTemplates`
- `createWatermarkTemplateForm`

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL until console assets are updated.

- [x] **Step 3: Implement console section**

Add nav link `Watermarks`, a create form with watermark template ID, name, pattern, a refresh button, and a table listing name, template ID, pattern, and created timestamp.

- [x] **Step 4: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Add Phase 5H notes for reusable watermark templates.

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

Run a temp server, create and list a watermark template through the admin API, verify `/admin/` includes Watermark templates, and check browser console errors with Playwright.

- [x] **Step 4: Commit**

Run:

```bash
git add README.md src/Drm.Server tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5h-watermark-template-management.md
git commit -m "feat: add watermark template management"
```

## Self-Review

- Spec coverage: Adds watermark pattern management from the approved enterprise DRM design.
- Security note: Admin-only tenant-scoped management; existing file enforcement behavior is unchanged.
- Placeholder scan: No TBD/TODO placeholders.
