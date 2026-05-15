# Phase 5R Apply Template Lease Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and superpowers:test-driven-development to implement this plan task-by-task.

**Goal:** Keep stored offline lease duration in sync when an admin applies a policy template to an existing protected file.

**Architecture:** Phase 5P stores the effective offline lease duration on `ProtectedFileEntity`. The existing apply-template endpoint already updates permissions, watermark, and owner grant. It should also copy `PolicyTemplateEntity.OfflineLeaseMinutes` to `ProtectedFileEntity.OfflineLeaseMinutes` so future policy decisions and key unwrap responses match the applied template.

## Tasks

- [x] **Step 1: Add failing API test**

Add a test in `AdminFilesApiTests` proving that applying a template with a non-default offline lease changes the allowed policy decision lease duration.

- [x] **Step 2: Run failing focused test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Apply_policy_template_updates_offline_lease_duration"
```

Expected: FAIL because apply-template currently leaves the existing file lease unchanged.

- [x] **Step 3: Implement lease sync**

Update `ApplyPolicyTemplateAsync` to assign `file.OfflineLeaseMinutes = template.OfflineLeaseMinutes`.

- [x] **Step 4: Run passing focused test**

Run the same filtered command. Expected: PASS.

- [x] **Step 5: Update README**

Document Phase 5R.

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
git commit -m "fix: sync applied template offline lease"
```
