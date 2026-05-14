# Phase 3B Offline Policy Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add short offline leases so the desktop viewer can use a cached allow decision only when the management server is temporarily unavailable.

**Architecture:** The server includes an `offlineLeaseExpiresAtUtc` field on successful policy decisions. Agent core stores allowed decisions in a local JSON cache keyed by tenant/file/user/device/action and only reuses them while the lease is still valid. `OpenProtectedPdfWorkflow` tries the server first and falls back to the cache only on transport failure.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, JSON file cache, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Endpoints/PolicyEndpoints.cs`: include `OfflineLeaseExpiresAtUtc` in `PolicyDecisionResponse`.
- Modify `tests/Drm.Server.Tests/PolicyApiTests.cs`: assert allowed responses include a short future lease and denied responses do not.
- Create `src/Drm.Agent.Core/PolicyDecisionCache.cs`: cache interface and JSON implementation.
- Modify `src/Drm.Agent.Core/DrmServerClient.cs`: parse the lease field into `OpenDecision`.
- Modify `src/Drm.Agent.Core/OpenProtectedPdfWorkflow.cs`: write allowed decisions to cache and read cache on server transport failure.
- Modify `tests/Drm.Agent.Core.Tests/ProtectAndOpenWorkflowTests.cs`: cover cache write and offline fallback behavior.
- Modify `README.md`: document offline lease behavior.

## Tasks

### Task 1: Server Lease Field

- [x] **Step 1: Write failing tests**

Add assertions:

```csharp
decision!.OfflineLeaseExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
decision.OfflineLeaseExpiresAtUtc.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(16));
```

For denied decisions:

```csharp
decision!.OfflineLeaseExpiresAtUtc.Should().BeNull();
```

- [x] **Step 2: Run failing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter PolicyApiTests`

Expected: compile failure because the response record has no `OfflineLeaseExpiresAtUtc`.

- [x] **Step 3: Implement server lease**

Set a default 15-minute offline lease on allowed policy decisions and null on denied/not-found/bad-request decisions.

- [x] **Step 4: Run passing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter PolicyApiTests`

Expected: PASS.

### Task 2: Agent Cache and Offline Fallback

- [x] **Step 1: Write failing tests**

Add tests that:
- server client parses `offlineLeaseExpiresAtUtc`;
- opening a file online writes an allowed lease to cache;
- opening a file with server throwing `HttpRequestException` succeeds with a valid cached allow decision;
- expired cache entries are denied offline.

- [x] **Step 2: Run failing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter Offline`

Expected: compile failure because `PolicyDecisionCache` and lease properties do not exist.

- [x] **Step 3: Implement cache**

Add `IPolicyDecisionCache` with `StoreAsync` and `TryGetAllowedAsync`. Implement `JsonPolicyDecisionCache` with atomic temp-file writes and no reuse after `OfflineLeaseExpiresAtUtc <= now`.

- [x] **Step 4: Wire open workflow**

`OpenProtectedPdfWorkflow` should:
- call the server first;
- store allowed decisions with non-null lease expiry;
- fallback to cache only for `HttpRequestException`;
- throw `UnauthorizedAccessException("Access denied: offline_lease_missing")` when no valid cached allow exists.

- [x] **Step 5: Run passing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter Offline`

Expected: PASS.

### Task 3: Docs and Verification

- [x] **Step 1: Document offline leases**

Update README with the 15-minute MVP lease, cache behavior, and deny-by-default behavior when the cache is absent or expired.

- [x] **Step 2: Run full verification**

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3b-offline-policy-cache.md
git commit -m "feat: add offline policy cache"
```

## Self-Review

- Spec coverage: Covers policy cache and short offline leases from Endpoint Controls and Policy Engine. Does not implement command polling, file deletion, or transparent folder encryption.
- Placeholder scan: No TBD/TODO placeholders.
- Type consistency: Uses `OfflineLeaseExpiresAtUtc`, `OpenDecision`, `IPolicyDecisionCache`, and `JsonPolicyDecisionCache` consistently.
