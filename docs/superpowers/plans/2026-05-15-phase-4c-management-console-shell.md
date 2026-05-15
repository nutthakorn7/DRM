# Phase 4C Management Console Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serve a usable browser-based management console shell from the management server at `/admin/`.

**Architecture:** Add static assets under `src/Drm.Server/wwwroot/admin/` and wire ASP.NET Core static files plus an `/admin` redirect. The shell stores the admin API key in `sessionStorage`, sets `X-DRM-Admin-Key` on admin API calls, lets operators set tenant context, list users, and create users.

**Tech Stack:** .NET 10 ASP.NET Core static files, vanilla HTML/CSS/JS, WebApplicationFactory tests.

---

## File Structure

- Modify `src/Drm.Server/Program.cs`: serve static files and redirect `/admin` to `/admin/`.
- Create `src/Drm.Server/wwwroot/admin/index.html`: management console markup.
- Create `src/Drm.Server/wwwroot/admin/app.css`: restrained operational UI styling.
- Create `src/Drm.Server/wwwroot/admin/app.js`: API-key session, tenant context, users list/create flows.
- Create `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: verifies `/admin/`, CSS, and JS assets are served.
- Modify `README.md` and `deploy/management/README.md`: document `/admin/`.

## Tasks

### Task 1: Console Serving Tests

- [x] **Step 1: Write failing tests**

Create tests that assert:
- `GET /admin` returns redirect to `/admin/`.
- `GET /admin/` returns HTML containing `DRM Management`, `X-DRM-Admin-Key`, `Tenant ID`, and asset references.
- `GET /admin/app.css` returns CSS containing `.workspace`.
- `GET /admin/app.js` returns JS containing `sessionStorage`, `X-DRM-Admin-Key`, and `/api/admin/users`.

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL because `/admin/` static console assets do not exist or are not served.

### Task 2: Static Console

- [x] **Step 1: Add static files and route**

Add the admin console HTML/CSS/JS assets and call `app.UseStaticFiles();` before admin auth middleware. Add `app.MapGet("/admin", () => Results.Redirect("/admin/"));`.

- [x] **Step 2: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Docs, Verification, Commit

- [x] **Step 1: Update docs**

Document `/admin/`, the `X-DRM-Admin-Key` header, and the fact that this is an MVP management shell.

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
git add src tests README.md deploy/management/README.md docs/superpowers/plans/2026-05-15-phase-4c-management-console-shell.md
git commit -m "feat: add management console shell"
```

## Self-Review

- Spec coverage: Adds a concrete management console entrypoint backed by existing admin APIs.
- Security note: Static shell is public, but admin API calls require the configured admin key.
- Placeholder scan: No TBD/TODO placeholders.
