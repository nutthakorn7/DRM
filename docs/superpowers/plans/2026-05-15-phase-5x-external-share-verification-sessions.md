# Phase 5X External Share Verification Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add guest verification sessions for external share links using a one-time code and short-lived verified session token, without returning file keys or decrypted content.

**Architecture:** Extend the existing public external-share API with `POST /api/share-links/verification/start` and `POST /api/share-links/verification/confirm`. Start validates the share token/email and active link/file state, generates a 6-digit code, stores only its hash, and sends the code via an injectable sender. Confirm validates the code, tracks failed attempts, stores only a session-token hash, and returns the plaintext session token once.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core, SHA-256 hashing helpers, xUnit/FluentAssertions, Microsoft dependency injection test overrides.

---

### Task 1: Verification Session Persistence And API

**Files:**
- Modify: `src/Drm.Server/Entities.cs`
- Modify: `src/Drm.Server/AppDbContext.cs`
- Create: `src/Drm.Server/ExternalShareVerificationCode.cs`
- Create: `src/Drm.Server/ExternalShareVerificationDelivery.cs`
- Modify: `src/Drm.Server/Program.cs`
- Modify: `src/Drm.Server/Endpoints/ExternalShareEndpoints.cs`
- Test: `tests/Drm.Server.Tests/ExternalShareApiTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests that:
- Start verification without `X-DRM-Client-Key`, assert the response omits the code, and assert the injected sender receives the code.
- Confirm verification with the delivered code, assert the response returns a one-time `verificationSessionToken`, and assert only its hash is stored.
- Assert wrong token/email returns `404` and does not send a code.
- Assert wrong code increments attempts, expired code is rejected, and exhausted attempts block confirmation.

- [ ] **Step 2: Run focused tests to verify RED**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareApiTests
```

Expected: FAIL because verification entities, sender, helpers, and endpoints do not exist.

- [ ] **Step 3: Implement minimal code**

Add `ExternalShareVerificationEntity` with tenant-scoped verification ID, share link ID, guest email, code hash, attempt counts, expiry, verified timestamp, session token hash, and session expiry. Add code generation/hash helper, a no-op production sender, DI registration, and verification endpoints.

- [ ] **Step 4: Run focused tests to verify GREEN**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ExternalShareApiTests
```

Expected: PASS.

### Task 2: Docs And Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document Phase 5X**

Add README notes for verification start/confirm endpoints, the sender abstraction, and the boundary that verified sessions are not browser viewer/key release yet.

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
git commit -m "feat: add external share verification sessions"
```

---

**Self-review**

- Spec coverage: Adds guest verification primitives without changing the no-key-release boundary.
- Placeholder scan: No placeholders remain.
- Type consistency: `verificationId`, `verificationSessionToken`, `guestEmail`, `attemptCount`, and reason codes are consistent across tests, API, and docs.
