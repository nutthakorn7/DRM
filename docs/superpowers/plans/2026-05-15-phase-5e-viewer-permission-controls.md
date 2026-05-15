# Phase 5E Viewer Permission Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows protected viewer expose print, copy, and export actions only when the opened file policy grants those permissions.

**Architecture:** Add a small `ViewerPermissionState` helper in agent core so permission gating is unit-tested outside WPF. The WPF viewer stores the current opened permissions/content, updates toolbar button states after load, blocks common keyboard shortcuts when permission is missing, and allows explicit export only when `ExportOriginal` is granted.

**Tech Stack:** .NET 10, WPF, agent core, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Agent.Core/ViewerPermissionState.cs`: map `Permission` flags to viewer action gates.
- Create `tests/Drm.Agent.Core.Tests/ViewerPermissionStateTests.cs`: cover print/copy/export gates.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml`: add Print, Copy, and Export buttons to the viewer toolbar.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml.cs`: track current permissions/content, update button states, block disallowed shortcuts, and implement export/print action guards.
- Modify `README.md`: document Phase 5E viewer permission controls and limits.
- Create `docs/superpowers/plans/2026-05-15-phase-5e-viewer-permission-controls.md`: this plan.

## Tasks

### Task 1: Permission State Helper

- [x] **Step 1: Write failing helper tests**

Create tests that assert:

- `Permission.None` denies print, copy, and export.
- `Permission.View | Permission.Print | Permission.Copy` allows print/copy but denies export.
- `Permission.View | Permission.ExportOriginal` allows export but denies print/copy.

- [x] **Step 2: Run failing helper tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ViewerPermissionStateTests
```

Expected: FAIL because `ViewerPermissionState` does not exist.

- [x] **Step 3: Implement helper**

Create `ViewerPermissionState` and `ViewerControlledAction` in `Drm.Agent.Core`. `From(Permission permissions)` should set `CanPrint`, `CanCopy`, and `CanExportOriginal` from the corresponding flags. `Allows(ViewerControlledAction action)` should return the matching boolean.

- [x] **Step 4: Run passing helper tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ViewerPermissionStateTests
```

Expected: PASS.

### Task 2: WPF Viewer Controls

- [x] **Step 1: Add toolbar buttons**

In `MainWindow.xaml`, add disabled-by-default `PrintButton`, `CopyButton`, and `ExportButton` controls in the viewer toolbar row.

- [x] **Step 2: Wire permission state**

In `MainWindow.xaml.cs`, store `currentPermissions` and `currentContent`, call `ApplyPermissionState()` after opening, and show `Permissions: ...` consistently.

- [x] **Step 3: Guard actions and shortcuts**

Implement:

- Print button: requires `Permission.Print`; calls the hosted PDF renderer print script when available.
- Export button: requires `Permission.ExportOriginal`; writes the decrypted PDF to a user-chosen path.
- Copy button: requires `Permission.Copy`; reports that copy is allowed through the embedded renderer when text selection is available.
- `Ctrl+P`, `Ctrl+S`, and `Ctrl+C` handling: print/export/copy shortcuts are blocked with a status message when the matching permission is missing.

- [x] **Step 4: Build viewer**

Run:

```bash
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Add Phase 5E notes explaining viewer-controlled actions and the client-side enforcement limit.

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
git add README.md src/Drm.Agent.Core src/Drm.Viewer.Windows tests/Drm.Agent.Core.Tests docs/superpowers/plans/2026-05-15-phase-5e-viewer-permission-controls.md
git commit -m "feat: add viewer permission controls"
```

## Self-Review

- Spec coverage: Implements viewer-level print/copy/export controls from the approved enterprise DRM design.
- Security note: This gates controls the viewer owns and blocks common shortcuts; it does not claim impossible screenshot prevention or kernel-level tamper resistance.
- Placeholder scan: No TBD/TODO placeholders.
