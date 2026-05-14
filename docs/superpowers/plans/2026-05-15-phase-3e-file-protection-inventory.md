# Phase 3E File Protection Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a file-based PDF protection workflow that writes protected containers to disk, records managed copies in inventory, and optionally deletes the original only after protection succeeds.

**Architecture:** Keep the existing byte-array `ProtectPdfWorkflow` as the cryptographic/registering primitive. Add a higher-level `ProtectPdfFileWorkflow` that validates the source PDF, chooses a `.drmx` destination, writes the protected container atomically, verifies the written container header, records inventory, and then optionally deletes the original source file.

**Tech Stack:** .NET 10, existing DRM container format, JSON protected-file inventory, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Agent.Core/ProtectPdfFileWorkflow.cs`: file-based protection orchestration.
- Modify `src/Drm.Agent.Core/ProtectedFileInventory.cs`: no behavior change expected, reused by file workflow.
- Create `tests/Drm.Agent.Core.Tests/ProtectPdfFileWorkflowTests.cs`: output creation, inventory registration, source-delete safety.
- Modify `README.md`: document `.drmx` protected output and original-delete safety.

## Tasks

### Task 1: File-Based Protection Workflow

- [x] **Step 1: Write failing tests**

Add tests that assert:
- protecting `report.pdf` writes `report.pdf.drmx`;
- the written output parses as a protected container;
- inventory contains the protected file ID and destination path;
- original source remains by default;
- original source is deleted only when `deleteOriginalAfterProtection: true`;
- when server registration fails, no output is committed and original source remains.

- [x] **Step 2: Run failing tests**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ProtectPdfFileWorkflow`

Expected: compile failure because `ProtectPdfFileWorkflow` does not exist.

- [x] **Step 3: Implement workflow**

Implement `ProtectPdfFileWorkflow` with:
- source existence check;
- `.pdf` extension check;
- destination default `${source}.drmx`;
- temp output `${destination}.{guid}.tmp`;
- container parse verification before final move;
- inventory upsert after final move;
- optional source delete after final move and inventory upsert.

- [x] **Step 4: Run passing tests**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ProtectPdfFileWorkflow`

Expected: PASS.

### Task 2: Docs and Verification

- [x] **Step 1: Document file protection**

Update README with `.drmx` output behavior, inventory registration, and original-delete safety semantics.

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3e-file-protection-inventory.md
git commit -m "feat: add file protection inventory workflow"
```

## Self-Review

- Spec coverage: Covers file protection handler foundation, managed protected-copy inventory, and safe original-delete behavior.
- Placeholder scan: No TBD/TODO placeholders.
- Type consistency: Uses `ProtectPdfFileWorkflow`, `.drmx`, and `JsonProtectedFileInventory` consistently.
