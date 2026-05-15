# Phase 5V External Share Link Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add admin-managed external share links with one-time token return, hashed token storage, expiry/max-use metadata, revocation, console controls, and audit events.

**Architecture:** Store external share links as tenant-scoped file children. Admin endpoints create, list, and revoke links, but no public decrypt or file-key release is added in this phase. A small token helper generates high-entropy URL-safe tokens and stores only SHA-256 hashes.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core, SQLite/Npgsql-compatible model configuration, static admin console HTML/CSS/JS, xUnit/FluentAssertions.

---

### Task 1: Server API And Persistence

**Files:**
- Modify: `src/Drm.Server/Entities.cs`
- Modify: `src/Drm.Server/AppDbContext.cs`
- Create: `src/Drm.Server/ExternalShareToken.cs`
- Modify: `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs`
- Test: `tests/Drm.Server.Tests/AdminFilesApiTests.cs`

- [ ] **Step 1: Write failing API tests**

Add tests that:
- Create a protected file, create an external share link, assert the response includes an access token once, and assert list responses omit the token.
- Assert the stored `TokenHash` is not the plaintext token.
- Assert wrong tenant and missing file return `404`.
- Assert expired links, invalid guest email, zero max uses, revoked files, and link expiry beyond file expiry are rejected with explicit reason codes.
- Revoke a link and assert the list response marks it revoked and audit contains `external_share_link_created` and `external_share_link_revoked`.

- [ ] **Step 2: Run focused tests to verify RED**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminFilesApiTests
```

Expected: FAIL because share-link endpoints and entity do not exist.

- [ ] **Step 3: Implement minimal server support**

Add `ExternalShareLinkEntity`, `DbSet<ExternalShareLinkEntity>`, EF key/index/length config, `ExternalShareToken`, and nested admin routes:

```text
POST /api/admin/files/{fileId}/share-links
GET /api/admin/files/{fileId}/share-links?tenantId=...
POST /api/admin/files/{fileId}/share-links/{shareLinkId}/revoke
```

Creation validates file tenant, not revoked, guest email, expiry, file expiry bound, and max uses. Creation returns the plaintext access token once. Listing and revoke responses never include token material.

- [ ] **Step 4: Run focused tests to verify GREEN**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AdminFilesApiTests
```

Expected: PASS.

### Task 2: Management Console

**Files:**
- Modify: `src/Drm.Server/wwwroot/admin/index.html`
- Modify: `src/Drm.Server/wwwroot/admin/app.js`
- Modify: `src/Drm.Server/wwwroot/admin/app.css`
- Test: `tests/Drm.Server.Tests/ManagementConsoleTests.cs`

- [ ] **Step 1: Write failing console asset tests**

Assert the console includes external share link UI markers:
- `External share links`
- `createShareLinkForm`
- `shareLinksBody`
- `/share-links`
- `refreshShareLinks`
- `revokeShareLink`

- [ ] **Step 2: Run console tests to verify RED**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL because the controls are not present.

- [ ] **Step 3: Implement console controls**

Add a create form for file ID, guest email, expiry, and max uses; add a refresh panel and table for link status; render the create response token once in a local output box. Add revoke buttons for active links.

- [ ] **Step 4: Run console tests to verify GREEN**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Docs, Verification, Commit

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document Phase 5V**

Add README notes for the new admin share-link endpoints and the security boundary that browser/public decrypt is intentionally not implemented yet.

- [ ] **Step 2: Run full verification**

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
git commit -m "feat: add external share link foundation"
```

---

**Self-review**

- Spec coverage: Covers the Phase 4 external sharing foundation by adding link lifecycle management without claiming browser viewer or guest identity verification.
- Placeholder scan: No placeholders or open-ended implementation steps remain.
- Type consistency: `shareLinkId`, `guestEmail`, `expiresAtUtc`, `maxUses`, `usedCount`, `revoked`, and audit reason codes are consistent across API, UI, tests, and docs.
