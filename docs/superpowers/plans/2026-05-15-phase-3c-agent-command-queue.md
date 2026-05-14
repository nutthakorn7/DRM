# Phase 3C Agent Command Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a tenant-scoped command queue so admins can request endpoint actions, starting with remote delete of managed protected copies.

**Architecture:** Admin APIs enqueue commands against a registered device and protected file. Agent APIs expose pending commands and allow completion/failure acknowledgement. Agent core adds typed client methods; local file deletion remains a later safe-delete handler that must verify a managed protected container before deleting.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Entities.cs`: add `AgentCommandEntity`.
- Modify `src/Drm.Server/AppDbContext.cs`: add `AgentCommands` DbSet and indexes.
- Modify `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs`: add admin endpoint to enqueue delete-protected-copy commands.
- Modify `src/Drm.Server/Endpoints/AgentEndpoints.cs`: add command polling and completion endpoints.
- Create `tests/Drm.Server.Tests/AgentCommandApiTests.cs`: verify enqueue, poll, complete, tenant isolation.
- Modify `src/Drm.Agent.Core/AgentIdentity.cs`: add `AgentCommand` and command completion records.
- Modify `src/Drm.Agent.Core/DrmServerClient.cs`: add command polling/completion methods.
- Modify `tests/Drm.Agent.Core.Tests/AgentClientTests.cs`: verify request shapes.
- Modify `README.md`: document command queue endpoints and local-delete safety boundary.

## Tasks

### Task 1: Server Command Queue

- [x] **Step 1: Write failing tests**

Add tests that:
- register a device and file;
- call `POST /api/admin/files/{fileId}/commands/delete-protected-copy`;
- poll `GET /api/agent/devices/{deviceId}/commands?tenantId=...`;
- complete via `POST /api/agent/devices/{deviceId}/commands/{commandId}/complete`;
- confirm completed commands no longer appear in pending results.

- [x] **Step 2: Run failing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AgentCommandApiTests`

Expected: compile or route failure because command entities/endpoints do not exist.

- [x] **Step 3: Implement server command queue**

Use command type `DeleteProtectedCopy`, status `Pending`, `Completed`, and `Failed`. Enqueue requires the file and device to exist in the tenant. Completion requires the command to match tenant/device and updates status, reason, and completed timestamp.

- [x] **Step 4: Run passing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AgentCommandApiTests`

Expected: PASS.

### Task 2: Agent Client Methods

- [x] **Step 1: Write failing tests**

Add tests that assert `DrmServerClient` calls:

```csharp
GET /api/agent/devices/{deviceId}/commands?tenantId={tenantId}
POST /api/agent/devices/{deviceId}/commands/{commandId}/complete
```

- [x] **Step 2: Run failing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter Command`

Expected: compile failure because command client types/methods do not exist.

- [x] **Step 3: Implement agent client methods**

Add `AgentCommand` and `AgentCommandCompletion` records and extend `IDrmServerClient` with `GetPendingCommandsAsync` and `CompleteCommandAsync`.

- [x] **Step 4: Run passing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter Command`

Expected: PASS.

### Task 3: Docs and Verification

- [x] **Step 1: Document command queue**

Update README with admin enqueue, agent poll, agent complete endpoints, and note that local deletion is not implemented until the agent has a safe protected-container verifier.

- [x] **Step 2: Run full verification**

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3c-agent-command-queue.md
git commit -m "feat: add agent command queue"
```

## Self-Review

- Spec coverage: Covers revocation/delete command queue and background-service polling contract. Does not perform local deletion yet.
- Placeholder scan: No TBD/TODO placeholders.
- Type consistency: Uses `AgentCommand`, `DeleteProtectedCopy`, and status values consistently.
