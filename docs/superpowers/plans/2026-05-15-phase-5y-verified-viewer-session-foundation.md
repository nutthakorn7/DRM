# Phase 5Y Verified Viewer Session Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a verified browser-viewer session metadata endpoint for external shares that validates the guest session token and returns safe viewer metadata without returning file keys or protected content.

**Architecture:** Extend `ExternalShareVerificationEntity` with a first-view timestamp so a verified session consumes the external link's max-use count at most once. Add `POST /api/share-links/viewer/session`, which hashes the supplied verification session token, tenant-scopes lookup, rechecks share/file/session state, increments `UsedCount` only on first viewer open for that verification, writes audit, and returns metadata for a download-disabled viewer shell.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core, existing token hashing helper, xUnit/FluentAssertions.

---

### Task 1: Verified Viewer Session API

**Files:**
- Modify: `src/Drm.Server/Entities.cs`
- Modify: `src/Drm.Server/AppDbContext.cs`
- Modify: `src/Drm.Server/Endpoints/ExternalShareEndpoints.cs`
- Test: `tests/Drm.Server.Tests/ExternalShareApiTests.cs`

- [x] **Step 1: Write failing tests**

Add tests that:
- Confirm verification, call `POST /api/share-links/viewer/session`, and assert the response contains file/viewer metadata only.
- Assert the response omits `verificationSessionToken`, token hashes, wrapped keys, ciphertext, and decrypted content.
- Assert the first viewer session call increments `UsedCount`, stores `ViewerOpenedAtUtc`, and writes `external_share_viewer_opened`.
- Assert repeated calls with the same verification session do not increment `UsedCount` again.
- Assert invalid session token returns `404`; expired session, revoked/expired link, exhausted max-use link, revoked file, and expired file return safe reason codes.

- [x] **Step 2: Run focused tests to verify RED**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareApiTests
```

Expected: FAIL because the viewer endpoint and `ViewerOpenedAtUtc` state do not exist.

- [x] **Step 3: Implement minimal code**

Add `ViewerOpenedAtUtc` to `ExternalShareVerificationEntity`, configure EF if needed, and implement `POST /api/share-links/viewer/session`. The endpoint returns only tenant/share/file IDs, guest email, content type, file/link/session expiry, watermark template, and fixed disabled-action flags.

- [x] **Step 4: Run focused tests to verify GREEN**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareApiTests
```

Expected: PASS.

### Task 2: Docs And Verification

**Files:**
- Modify: `README.md`

- [x] **Step 1: Document Phase 5Y**

Add README notes for `POST /api/share-links/viewer/session`, including the boundary that the endpoint prepares a browser-viewer session but still does not release keys or content.

- [x] **Step 2: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

Expected: all commands exit 0.

- [ ] **Step 3: Commit**

Commit message:

```bash
git commit -m "feat: add verified viewer session foundation"
```

---

**Self-review**

- Spec coverage: Adds browser-viewer session metadata foundation while preserving the no-key/no-content boundary.
- Placeholder scan: No placeholders remain.
- Type consistency: `verificationSessionToken`, `ViewerOpenedAtUtc`, `downloadDisabled`, and reason codes are consistent across tests, API, and docs.
