# Phase 5F Viewer Action Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record audit evidence when the protected viewer allows or blocks user-controlled copy, print, and export actions.

**Architecture:** Add a tested `ViewerActionAudit` factory that maps viewer actions and allow/block outcomes into existing `AgentAuditRecord` event types accepted by `/api/agent/audit`. Extend `OpenedProtectedPdf` with tenant/file IDs so the WPF viewer can audit against the opened file, then have viewer action handlers upload audit records through `DrmServerClient`.

**Tech Stack:** .NET 10, WPF, agent core, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Agent.Core/ViewerActionAudit.cs`: build audit records for viewer actions.
- Create `tests/Drm.Agent.Core.Tests/ViewerActionAuditTests.cs`: cover event and reason mapping.
- Modify `src/Drm.Agent.Core/OpenProtectedPdfWorkflow.cs`: include tenant/file IDs in `OpenedProtectedPdf`.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml.cs`: store opened identity/file metadata and upload audit events for viewer actions.
- Modify `README.md`: document viewer action audit events.
- Create `docs/superpowers/plans/2026-05-15-phase-5f-viewer-action-audit.md`: this plan.

## Tasks

### Task 1: Viewer Action Audit Factory

- [x] **Step 1: Write failing tests**

Create tests that assert:

- Allowed print creates `print_allowed/allowed`.
- Blocked copy creates `copy_blocked/missing_copy_permission`.
- Blocked export creates `export_blocked/missing_export_permission`.
- The record carries tenant, user, device, file, and supplied timestamp.

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ViewerActionAuditTests
```

Expected: FAIL because `ViewerActionAudit` does not exist.

- [x] **Step 3: Implement factory**

Create `ViewerActionAudit.Create(identity, fileId, action, allowed, atUtc)` and map events/reasons conservatively:

- print allowed: `print_allowed/allowed`
- print blocked: `print_blocked/missing_print_permission`
- copy allowed: `copy_allowed/allowed`
- copy blocked: `copy_blocked/missing_copy_permission`
- export allowed: `export_allowed/allowed`
- export blocked: `export_blocked/missing_export_permission`

- [x] **Step 4: Run passing factory tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ViewerActionAuditTests
```

Expected: PASS.

### Task 2: Viewer Wiring

- [x] **Step 1: Add opened metadata**

Extend `OpenedProtectedPdf` to include `TenantId` and `FileId`, populated from the protected package header in `OpenWithDecision`.

- [x] **Step 2: Wire viewer audit upload**

In `MainWindow.xaml.cs`, store current server URL, client API key, `AgentIdentity`, and file ID after open. Add `AuditViewerActionAsync(action, allowed)` that creates a fresh `DrmServerClient` and calls `UploadAuditAsync`.

- [x] **Step 3: Audit action handlers**

Emit:

- allowed audit after Copy button is accepted by policy.
- allowed audit after Print is requested by policy.
- allowed audit after Export writes the PDF.
- blocked audit when `Ctrl+C`, `Ctrl+P`, or `Ctrl+S` is blocked by missing permission.

- [x] **Step 4: Build viewer**

Run:

```bash
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Add Phase 5F notes for `copy_*`, `print_*`, and `export_*` endpoint audit events.

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
git add README.md src/Drm.Agent.Core src/Drm.Viewer.Windows tests/Drm.Agent.Core.Tests docs/superpowers/plans/2026-05-15-phase-5f-viewer-action-audit.md
git commit -m "feat: audit viewer actions"
```

## Self-Review

- Spec coverage: Adds audit evidence for viewer print/copy/export activity.
- Security note: Uses existing endpoint audit ingestion and accepted `print_`, `copy_`, and `export_` prefixes.
- Placeholder scan: No TBD/TODO placeholders.
