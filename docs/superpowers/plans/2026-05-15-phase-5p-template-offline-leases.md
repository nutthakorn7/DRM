# Phase 5P Template Offline Leases Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans and superpowers:test-driven-development to implement this plan task-by-task.

**Goal:** Make policy template `offlineLeaseMinutes` affect the offline lease returned by policy decisions and file-key unwraps.

**Architecture:** Store the effective offline lease duration on each registered protected file. Files created without a template keep the existing 15-minute default. Files created with a template inherit that template's lease minutes. A zero-minute template disables offline lease issuance by returning `null` for `offlineLeaseExpiresAtUtc` while still allowing online access.

## Tasks

### Task 1: Policy Tests

- [x] **Step 1: Add failing tests**

Add policy API tests proving:

- a file registered with a 45-minute template returns an offline lease close to decision time plus 45 minutes;
- a file registered with a zero-minute template returns `offlineLeaseExpiresAtUtc = null` on an allowed decision.

- [x] **Step 2: Run the failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Template_offline_lease"
```

Expected: FAIL because the server currently returns the fixed 15-minute lease.

### Task 2: Implementation

- [x] **Step 1: Persist effective lease duration**

Add `OfflineLeaseMinutes` to `ProtectedFileEntity`, configure a default of 15, and set it from the effective registration policy in `FilesEndpoints`.

- [x] **Step 2: Use lease duration in policy decisions**

Update `PolicyDecisionService` so allowed decisions return:

- `decisionTime.AddMinutes(file.OfflineLeaseMinutes)` when minutes are greater than zero;
- `null` when the effective lease is zero.

- [x] **Step 3: Run passing focused tests**

Run the same filtered command. Expected: PASS.

### Task 3: Documentation and Verification

- [x] **Step 1: Update README**

Document Phase 5P.

- [x] **Step 2: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

- [x] **Step 3: Commit**

Commit as:

```bash
git commit -m "feat: honor template offline leases"
```
