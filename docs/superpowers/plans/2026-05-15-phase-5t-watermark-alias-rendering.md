# Phase 5T Watermark Alias Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and superpowers:test-driven-development to implement this plan task-by-task.

**Goal:** Render common `{userId}` and `{fileId}` watermark placeholders in the desktop open workflow.

**Architecture:** `OpenProtectedFileWorkflow.ApplyWatermark` already renders `{user}`, `{file}`, and `{time}`. Extend it to treat `{userId}` as an alias for the user GUID and `{fileId}` as an alias for the protected file GUID. This keeps existing templates working while making policy-template examples and admin-entered watermark text render as expected in the viewer.

## Tasks

- [x] **Step 1: Add failing agent-core test**

Add a test proving `{userId}`, `{fileId}`, and `{time}` are replaced in the opened file watermark and no raw alias placeholders remain.

- [x] **Step 2: Run failing focused test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "OpenProtectedPdfFileWorkflow_renders_watermark_alias_placeholders"
```

Expected: FAIL because `{userId}` and `{fileId}` are not currently replaced.

- [x] **Step 3: Implement alias rendering**

Update `ApplyWatermark` to replace `{userId}` and `{fileId}` in addition to existing placeholders.

- [x] **Step 4: Run passing focused test**

Run the same filtered command. Expected: PASS.

- [x] **Step 5: Update README**

Document Phase 5T.

- [x] **Step 6: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

- [x] **Step 7: Commit**

Commit as:

```bash
git commit -m "fix: render watermark id aliases"
```
