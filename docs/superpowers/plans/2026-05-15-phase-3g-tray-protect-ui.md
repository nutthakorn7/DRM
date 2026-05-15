# Phase 3G Tray Protect UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Windows tray app useful by adding a minimal PDF protection workflow UI backed by agent core.

**Architecture:** The tray app remains a thin WPF shell. It gathers server URL, tenant ID, user ID, source PDF path, and original-delete preference, then calls `ProtectPdfFileWorkflow` with `DrmServerClient`, `JsonProtectedFileInventory`, and `JsonFileKeyStore`. Inventory/key files use `%ProgramData%\DRM`.

**Tech Stack:** WPF, .NET 10 Windows target, existing agent core workflows.

---

## File Structure

- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml`: add compact operational form and status area.
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`: implement browse/protect handlers.
- Modify `README.md`: document tray MVP usage.

## Tasks

### Task 1: Tray Protect Form

- [x] **Step 1: Implement XAML form**

Add inputs for server URL, tenant ID, user ID, selected PDF, delete-original checkbox, browse button, protect button, and status text.

- [x] **Step 2: Implement code-behind**

Use `OpenFileDialog`, `DrmServerClient`, `JsonProtectedFileInventory`, `JsonFileKeyStore`, `ProtectPdfFileWorkflow`, and `EnvelopeCrypto.GenerateKey()`.

- [x] **Step 3: Build tray app**

Run: `/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj`

Expected: PASS.

### Task 2: Docs, Verification, Commit

- [x] **Step 1: Document tray MVP**

Update README with tray app fields and output behavior.

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
git add src/Drm.Agent.Tray.Windows README.md docs/superpowers/plans/2026-05-15-phase-3g-tray-protect-ui.md
git commit -m "feat: add tray protect workflow"
```

## Self-Review

- Spec coverage: Adds visible desktop client entry point for users to protect PDF files.
- Scope limit: Does not add installer, shell extension, auth, or production key wrapping.
- Placeholder scan: No TBD/TODO placeholders.
