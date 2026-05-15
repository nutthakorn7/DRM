# Phase 5W External Share Redemption Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a public guest redemption endpoint that verifies a share token and guest email, consumes max-use count, returns safe file metadata, and never releases file keys or decrypted content.

**Architecture:** Keep admin link management from Phase 5V unchanged. Add a narrow public route, `POST /api/share-links/redeem`, that is explicitly exempt from client API-key middleware. The route hashes the submitted token, tenant-scopes lookup, validates guest email, expiry, revocation, max-use count, file expiry/revocation, increments `UsedCount`, and writes an audit event.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core, existing `ExternalShareToken` hashing helper, xUnit/FluentAssertions.

---

### Task 1: Public Redemption API

**Files:**
- Create: `src/Drm.Server/Endpoints/ExternalShareEndpoints.cs`
- Modify: `src/Drm.Server/Program.cs`
- Modify: `src/Drm.Server/ClientApiKeyAuthentication.cs`
- Test: `tests/Drm.Server.Tests/ExternalShareApiTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests that:
- Create a protected file and external share link, then redeem with tenant ID, access token, and guest email.
- Assert redemption succeeds without `X-DRM-Client-Key` even when `Drm:Security:ClientApiKey` is configured.
- Assert success returns file metadata and no `accessToken`, `tokenHash`, wrapped key, or decrypted data.
- Assert success increments `UsedCount` and writes `external_share_accessed/external_share_link_redeemed`.
- Assert wrong guest email or wrong token returns `404`.
- Assert revoked, expired, max-used, revoked-file, and expired-file states return explicit safe reason codes and do not increment `UsedCount`.

- [ ] **Step 2: Run focused tests to verify RED**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareApiTests
```

Expected: FAIL because the endpoint and client-key exemption do not exist.

- [ ] **Step 3: Implement minimal API**

Create `ExternalShareEndpoints` with `MapExternalShareEndpoints()` and map:

```text
POST /api/share-links/redeem
```

Use request fields `tenantId`, `accessToken`, and `guestEmail`. Hash `accessToken` with `ExternalShareToken.Hash`, find the tenant-scoped link, validate state, increment use count on success, and return only metadata: tenant ID, share link ID, file ID, guest email, content type, expiry, max uses, used count, and reason code.

- [ ] **Step 4: Run focused tests to verify GREEN**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareApiTests
```

Expected: PASS.

### Task 2: Docs And Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document Phase 5W**

Add README notes for `POST /api/share-links/redeem`, including the security boundary that redemption returns metadata only and does not provide file keys or browser viewing yet.

- [ ] **Step 2: Full verification**

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
git commit -m "feat: add external share redemption foundation"
```

---

**Self-review**

- Spec coverage: Implements public token/email verification and max-use consumption while preserving the no-key-release boundary.
- Placeholder scan: No placeholder instructions remain.
- Type consistency: Endpoint, tests, and docs use `tenantId`, `accessToken`, `guestEmail`, `usedCount`, and `reasonCode` consistently.
