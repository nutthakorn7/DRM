# Phase 3D Safe Command Processor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an agent-side command processor that safely handles `DeleteProtectedCopy` commands only for inventoried protected containers.

**Architecture:** The agent maintains a local JSON inventory of managed protected file paths. The command processor polls the server, verifies each delete target is in inventory and that the local file parses as a protected container with matching tenant/file IDs, deletes it, removes inventory, and acknowledges the command. Non-matching or missing files are reported as failed and left untouched.

**Tech Stack:** .NET 10, JSON file inventory, protected container reader, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Agent.Core/ProtectedFileInventory.cs`: inventory records, interface, and JSON implementation.
- Create `src/Drm.Agent.Core/AgentCommandProcessor.cs`: command polling and safe delete handling.
- Create `tests/Drm.Agent.Core.Tests/AgentCommandProcessorTests.cs`: safe delete, verification failure, missing inventory tests.
- Modify `src/Drm.Agent.Service.Windows/AgentServiceOptions.cs`: add `InventoryPath`.
- Modify `src/Drm.Agent.Service.Windows/Program.cs`: register inventory and command processor.
- Modify `src/Drm.Agent.Service.Windows/Worker.cs`: process pending commands after heartbeat.
- Modify `README.md`: document safe delete behavior.

## Tasks

### Task 1: Agent Inventory and Processor

- [x] **Step 1: Write failing tests**

Add tests that assert:
- a verified protected container is deleted and command completes with `Completed/deleted`;
- a non-container file is not deleted and command completes with `Failed/verification_failed`;
- a command without inventory completes with `Failed/not_found`.

- [x] **Step 2: Run failing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter AgentCommandProcessor`

Expected: compile failure because inventory and processor types do not exist.

- [x] **Step 3: Implement inventory and processor**

Implement `JsonProtectedFileInventory` with `UpsertAsync`, `FindAsync`, and `RemoveAsync`. Implement `AgentCommandProcessor.ProcessPendingAsync` so it only deletes after `ProtectedFileReader.Read` succeeds and header tenant/file IDs match the command.

- [x] **Step 4: Run passing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter AgentCommandProcessor`

Expected: PASS.

### Task 2: Service Wiring

- [x] **Step 1: Wire service dependencies**

Register `JsonProtectedFileInventory` using `DrmAgent:InventoryPath` and register `AgentCommandProcessor`. Worker should call `ProcessPendingAsync` after the heartbeat workflow.

- [x] **Step 2: Build service**

Run: `/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj`

Expected: PASS.

### Task 3: Docs, Verification, Commit

- [x] **Step 1: Document safe delete**

Update README with inventory requirement and protected-container verification behavior.

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3d-safe-command-processor.md
git commit -m "feat: add safe agent command processor"
```

## Self-Review

- Spec coverage: Adds remote protected-copy delete handling within the non-goal boundary: no arbitrary file deletion.
- Placeholder scan: No TBD/TODO placeholders.
- Type consistency: Uses `ProtectedFileInventoryEntry`, `JsonProtectedFileInventory`, and `AgentCommandProcessor` consistently.
