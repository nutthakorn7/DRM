# Phase 3I Server Key Wrapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add server-side file-key wrapping and policy-gated unwrap so desktop clients no longer need local JSON keys as the only key source.

**Architecture:** The server stores each file key encrypted with a tenant-scoped wrapping key derived from a configured master key. The MVP uses an in-process development master key when no config is present, and documents that production must configure a durable KMS/HSM-backed key. Unwrap calls evaluate policy first and return the raw file key only when the requested permission is allowed.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core, AES-GCM, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Entities.cs`: add `FileKeyEntity`.
- Modify `src/Drm.Server/AppDbContext.cs`: add `FileKeys` DbSet and model config.
- Create `src/Drm.Server/FileKeyProtection.cs`: tenant wrapping-key derivation and AES-GCM wrap/unwrap.
- Create `src/Drm.Server/PolicyDecisionService.cs`: reusable server-side policy decision logic.
- Modify `src/Drm.Server/Endpoints/PolicyEndpoints.cs`: use `PolicyDecisionService`.
- Create `src/Drm.Server/Endpoints/FileKeyEndpoints.cs`: add wrap and unwrap endpoints.
- Modify `src/Drm.Server/Program.cs`: register services and map endpoints.
- Create `tests/Drm.Server.Tests/FileKeyApiTests.cs`: wrap/unwrap policy gating.
- Modify `src/Drm.Agent.Core/DrmServerClient.cs`: add file-key wrap/unwrap methods.
- Modify `tests/Drm.Agent.Core.Tests/AgentClientTests.cs`: verify client request shapes.
- Modify `README.md`: document key wrapping MVP and production warning.

## Tasks

### Task 1: Server Key APIs

- [x] **Step 1: Write failing tests**

Add tests that:
- register a file, wrap its key, then unwrap as an allowed owner and get the same key back;
- unwrap as a denied user returns 403;
- unwrap a missing wrapped key returns 404;
- duplicate wrap for a file replaces the previous key.

- [x] **Step 2: Run failing tests**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter FileKeyApiTests`

Expected: route/entity failures because key APIs do not exist.

- [x] **Step 3: Implement services and endpoints**

Implement:
- `POST /api/files/{fileId}/keys/wrap`
- `POST /api/files/{fileId}/keys/unwrap`
- AES-GCM wrapping with associated data `tenantId:fileId`;
- policy-gated unwrap using requested permission.

- [x] **Step 4: Run passing tests**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter FileKeyApiTests`

Expected: PASS.

### Task 2: Agent Client Methods

- [x] **Step 1: Write failing tests**

Add tests that assert `DrmServerClient` posts:

```csharp
POST /api/files/{fileId}/keys/wrap
POST /api/files/{fileId}/keys/unwrap
```

- [x] **Step 2: Run failing tests**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter FileKeyClient`

Expected: compile failure because client methods do not exist.

- [x] **Step 3: Implement client methods**

Add `WrapFileKeyAsync` and `UnwrapFileKeyAsync` to `IDrmServerClient` and `DrmServerClient`.

- [x] **Step 4: Run passing tests**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter FileKeyClient`

Expected: PASS.

### Task 3: Docs, Verification, Commit

- [x] **Step 1: Document key wrapping**

Update README with key wrapping endpoints and the production KMS warning.

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3i-server-key-wrapping.md
git commit -m "feat: add server key wrapping"
```

## Self-Review

- Spec coverage: Adds key wrapping and policy-gated unwrap foundation from the Key Management/API domains.
- Security note: Development master key fallback is not production-grade; production must use durable configured/KMS-backed tenant keys.
- Placeholder scan: No TBD/TODO placeholders.
