# Phase 4B Admin API Key Auth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a management API authentication gate so `/api/admin/*` endpoints are protected when an admin API key is configured.

**Architecture:** Add lightweight ASP.NET Core middleware that checks `X-DRM-Admin-Key` for `/api/admin/*` paths only when `Drm:Security:AdminApiKey` is set. Existing development/test flows without the config remain unchanged, while the on-prem install script requires `DRM_ADMIN_API_KEY` and exports it into configuration.

**Tech Stack:** .NET 10, ASP.NET Core middleware, WebApplicationFactory, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Server/AdminApiKeyAuthentication.cs`: middleware extension and constant-time API key comparison.
- Modify `src/Drm.Server/Program.cs`: add middleware before endpoint mappings.
- Create `tests/Drm.Server.Tests/AdminApiKeyAuthenticationTests.cs`: verifies 401/403/200 behavior.
- Modify `deploy/management/appsettings.onprem.example.json`: add admin key placeholder.
- Modify `deploy/management/start-management.sh`: require and export `DRM_ADMIN_API_KEY`.
- Modify `deploy/management/README.md` and `README.md`: document admin API key requirement.
- Modify `tests/Drm.Server.Tests/ManagementInstallAssetsTests.cs`: verify install assets include admin API key safeguards.

## Tasks

### Task 1: Admin Auth Tests

- [x] **Step 1: Write failing tests**

Create tests that configure `Drm:Security:AdminApiKey = secret-admin-key` and assert:
- `GET /api/admin/users?tenantId=<guid>` without `X-DRM-Admin-Key` returns 401.
- The same request with the wrong key returns 403.
- The same request with `X-DRM-Admin-Key: secret-admin-key` returns 200.
- `GET /healthz` remains 200 without a key.

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminApiKeyAuthenticationTests
```

Expected: FAIL because admin endpoints do not enforce API key authentication yet.

### Task 2: Middleware Implementation

- [x] **Step 1: Implement middleware**

Add `AdminApiKeyAuthentication` middleware that:
- only applies to paths starting `/api/admin`
- skips auth when `Drm:Security:AdminApiKey` is blank
- returns 401 with `reasonCode = admin_api_key_required` when the header is missing
- returns 403 with `reasonCode = admin_api_key_invalid` when the header is wrong
- uses `CryptographicOperations.FixedTimeEquals` for the configured/submitted key comparison

- [x] **Step 2: Wire Program**

Call `app.UseAdminApiKeyAuthentication();` after app creation and before endpoint mapping.

- [x] **Step 3: Run passing auth tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminApiKeyAuthenticationTests
```

Expected: PASS.

### Task 3: Install Asset Updates

- [x] **Step 1: Update install asset tests**

Extend `ManagementInstallAssetsTests` to assert the example config and start script include `Drm:Security:AdminApiKey`, `DRM_ADMIN_API_KEY`, and `Drm__Security__AdminApiKey`.

- [x] **Step 2: Run failing install tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementInstallAssetsTests
```

Expected: FAIL until install assets are updated.

- [x] **Step 3: Update config/script/docs**

Add the admin API key placeholder to the example config, require `DRM_ADMIN_API_KEY` in `start-management.sh`, export `Drm__Security__AdminApiKey`, and document the required header.

- [x] **Step 4: Run passing install tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementInstallAssetsTests
```

Expected: PASS.

### Task 4: Full Verification and Commit

- [x] **Step 1: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 2: Commit**

Run:

```bash
git add src tests deploy README.md docs/superpowers/plans/2026-05-15-phase-4b-admin-api-key-auth.md
git commit -m "feat: protect admin APIs with API key"
```

## Self-Review

- Spec coverage: Adds the first management API authentication gate without breaking existing unauthenticated development tests.
- Security note: API key auth is a baseline gate; production still needs TLS, rotation, audit, and stronger identity integration.
- Placeholder scan: No TBD/TODO placeholders.
