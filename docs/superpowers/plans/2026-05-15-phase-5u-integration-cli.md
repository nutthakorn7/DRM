# Phase 5U Integration CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and superpowers:test-driven-development to implement this plan task-by-task.

**Goal:** Add a small automation CLI for integration workflows that can protect source files and open protected containers through the existing agent-core workflows.

**Architecture:** Create `src/Drm.Cli` as a .NET console app referencing `Drm.Agent.Core` and `Drm.Crypto`. The CLI parses `protect` and `open` commands with explicit IDs/paths/server URL, then delegates to `ProtectFileWorkflow` or `OpenProtectedFileWorkflow`. Keep parsing isolated and unit-tested in `tests/Drm.Cli.Tests`; avoid adding new server behavior.

## Tasks

- [x] **Step 1: Add failing CLI parser tests**

Create tests for:

- parsing `protect` with server URL, tenant/user IDs, file path, policy template, recipients, permissions, and delete-original flag;
- parsing `open` with server URL, user/device IDs, file path, and output path;
- rejecting unknown commands and missing required fields.

- [x] **Step 2: Run failing focused tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Cli.Tests/Drm.Cli.Tests.csproj
```

Expected: FAIL because `Drm.Cli` is not implemented yet.

- [x] **Step 3: Implement CLI parser**

Add `CliParser`, typed command option records, repeated recipient parsing, and a minimal usage string.

- [x] **Step 4: Wire protect/open execution**

Add `Program`/`DrmCli` execution:

- `protect` generates a file key, registers/wraps/protects the file, saves inventory/key metadata, and prints the `.drmx` path.
- `open` unwraps/opens the protected file and writes decrypted bytes to the requested output path.

- [x] **Step 5: Add projects to solution**

Add `Drm.Cli` and `Drm.Cli.Tests` to `Drm.sln`.

- [x] **Step 6: Run focused CLI tests**

Expected: PASS.

- [ ] **Step 7: Update README**

Document Phase 5U with example commands.

- [x] **Step 8: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

- [x] **Step 9: Commit**

Commit as:

```bash
git commit -m "feat: add integration CLI"
```
