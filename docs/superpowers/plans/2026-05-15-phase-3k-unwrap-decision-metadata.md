# Phase 3K Unwrap Decision Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return policy decision metadata with server file-key unwrap responses so desktop open does not need to call `/api/policy/decide` a second time after a successful unwrap.

**Architecture:** The unwrap endpoint already evaluates policy before releasing a key. It will include `allowedPermissions`, `watermarkTemplate`, and `offlineLeaseExpiresAtUtc` in its success response. The agent client will parse this into a typed result, and `OpenProtectedPdfFileWorkflow` will decrypt and watermark directly from that unwrap result while still using the old policy decision path for local-key offline fallback.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, agent core workflows, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Endpoints/FileKeyEndpoints.cs`: include decision metadata in successful unwrap responses.
- Modify `src/Drm.Agent.Core/DrmServerClient.cs`: change file-key unwrap client method from `byte[]` to a typed result with key and decision metadata.
- Modify `src/Drm.Agent.Core/OpenProtectedPdfWorkflow.cs`: expose the existing decrypt/watermark helper for reuse by file-path open workflow.
- Modify `src/Drm.Agent.Core/OpenProtectedPdfFileWorkflow.cs`: use unwrap metadata directly and store offline leases in `IPolicyDecisionCache` without a second policy decision call.
- Modify tests under `tests/Drm.Server.Tests` and `tests/Drm.Agent.Core.Tests`: cover response metadata, client parsing, no duplicate decide, and fake interface updates.
- Modify `README.md`: document that unwrap returns decision metadata and avoids a second policy call.

## Tasks

### Task 1: Server Unwrap Metadata

- [x] **Step 1: Write failing server test**

In `tests/Drm.Server.Tests/FileKeyApiTests.cs`, extend `Allowed_owner_can_wrap_and_unwrap_file_key` to parse the unwrap JSON and assert:
- `allowedPermissions` is `View`
- `watermarkTemplate` is `user:{userId}`
- `offlineLeaseExpiresAtUtc` is present and greater than the request time

- [x] **Step 2: Run failing server test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter FileKeyApiTests
```

Expected: FAIL because successful unwrap responses do not yet include decision metadata.

- [x] **Step 3: Implement server response fields**

In `src/Drm.Server/Endpoints/FileKeyEndpoints.cs`, add `AllowedPermissions`, `WatermarkTemplate`, and `OfflineLeaseExpiresAtUtc` to `UnwrapFileKeyResponse`, populated from `PolicyDecisionService.DecideAsync`.

- [x] **Step 4: Run passing server test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter FileKeyApiTests
```

Expected: PASS.

### Task 2: Agent Client Typed Unwrap Result

- [x] **Step 1: Write failing client test**

In `tests/Drm.Agent.Core.Tests/AgentClientTests.cs`, update `FileKeyClient_unwraps_file_key` to return JSON with:
- `allowedPermissions: "View, Print"`
- `watermarkTemplate: "{user} {file}"`
- `offlineLeaseExpiresAtUtc: "2026-05-15T01:00:00Z"`

Assert `UnwrapFileKeyAsync` returns a typed result with the original key, parsed permissions, watermark, and lease expiry.

- [x] **Step 2: Run failing client test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter FileKeyClient
```

Expected: FAIL because `UnwrapFileKeyAsync` still returns only `byte[]`.

- [x] **Step 3: Implement typed client result**

Add:

```csharp
public sealed record UnwrappedFileKey(
    byte[] FileKey,
    Permission AllowedPermissions,
    string? WatermarkTemplate,
    DateTimeOffset? OfflineLeaseExpiresAtUtc);
```

Change `IDrmServerClient.UnwrapFileKeyAsync` and `DrmServerClient.UnwrapFileKeyAsync` to return `Task<UnwrappedFileKey>`.

- [x] **Step 4: Run passing client test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter FileKeyClient
```

Expected: PASS.

### Task 3: Desktop Open Avoids Duplicate Policy Decision

- [x] **Step 1: Write failing workflow test**

In `tests/Drm.Agent.Core.Tests/OpenProtectedPdfFileWorkflowTests.cs`, add:
- `OpenProtectedPdfFileWorkflow_uses_unwrap_decision_without_second_policy_call`

Configure the fake server so `DecideAsync` throws when `FailDecision` is true, while `UnwrapFileKeyAsync` returns key and metadata. Assert open still succeeds and stores a cache entry when an offline lease is present.

- [x] **Step 2: Run failing workflow test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter OpenProtectedPdfFileWorkflow
```

Expected: FAIL because `OpenProtectedPdfFileWorkflow` calls `OpenProtectedPdfWorkflow`, which calls `DecideAsync` after unwrap.

- [x] **Step 3: Implement direct open from unwrap metadata**

Make `OpenProtectedPdfWorkflow.OpenWithDecision` public static or internal static. In `OpenProtectedPdfFileWorkflow`, when server unwrap succeeds:
- store the unwrap lease in `decisionCache` if present
- call `OpenProtectedPdfWorkflow.OpenWithDecision(...)`
- skip `serverClient.DecideAsync`

Keep the local-key fallback path using `OpenProtectedPdfWorkflow` so offline policy cache behavior remains intact.

- [x] **Step 4: Run passing workflow test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter OpenProtectedPdfFileWorkflow
```

Expected: PASS.

### Task 4: Full Verification and Commit

- [x] **Step 1: Update README**

Document that successful key unwrap returns decision metadata and that desktop online open no longer makes a second policy decision call.

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3k-unwrap-decision-metadata.md
git commit -m "feat: return decision metadata with file key unwrap"
```

## Self-Review

- Spec coverage: Removes duplicate policy decision for successful server unwrap while preserving policy-gated key release.
- Security note: The unwrap endpoint remains the key-release authorization gate; local fallback still requires offline policy validation.
- Placeholder scan: No TBD/TODO placeholders.
