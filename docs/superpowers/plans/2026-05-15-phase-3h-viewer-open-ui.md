# Phase 3H Viewer Open UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows protected viewer open `.drmx` files using the local key store and server policy decision.

**Architecture:** Keep the viewer as a thin WPF shell. It gathers server URL, user ID, device ID, and protected-file path, then calls `OpenProtectedPdfFileWorkflow`. Decrypted PDF bytes are written to a temporary PDF file and loaded into the existing browser surface with the returned watermark and permissions.

**Tech Stack:** WPF, .NET 10 Windows target, existing agent core open workflow.

---

## File Structure

- Modify `src/Drm.Viewer.Windows/MainWindow.xaml`: add compact open controls above viewer surface.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml.cs`: implement browse/open handlers, temp PDF handling, local key-store path.
- Modify `README.md`: document viewer MVP.

## Tasks

### Task 1: Viewer Open Workflow

- [x] **Step 1: Implement XAML controls**

Add server URL, user ID, device ID, `.drmx` path, browse button, and open button.

- [x] **Step 2: Implement code-behind**

Use `OpenFileDialog`, `DrmServerClient`, `JsonFileKeyStore`, `OpenProtectedPdfFileWorkflow`, and temp PDF output. Delete prior temp PDF when loading a new file or closing.

- [x] **Step 3: Build viewer**

Run: `/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1`

Expected: PASS.

### Task 2: Docs, Verification, Commit

- [x] **Step 1: Document viewer MVP**

Update README with `.drmx` open behavior and local key-store dependency.

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
git add src/Drm.Viewer.Windows README.md docs/superpowers/plans/2026-05-15-phase-3h-viewer-open-ui.md
git commit -m "feat: add viewer open workflow"
```

## Self-Review

- Spec coverage: Adds visible protected viewer open path for protected PDF containers.
- Scope limit: Does not yet enforce copy/print/export controls beyond returned permissions display.
- Placeholder scan: No TBD/TODO placeholders.
