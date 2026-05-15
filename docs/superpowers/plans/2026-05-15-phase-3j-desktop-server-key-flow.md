# Phase 3J Desktop Server Key Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make desktop protect/open workflows use the server key wrapping APIs as the primary key path, while keeping the local JSON key store only as an offline fallback.

**Architecture:** `ProtectPdfFileWorkflow` registers the file, wraps the file key on the management server, then writes the protected output and optional local fallback key. `OpenProtectedPdfFileWorkflow` reads the protected header, asks the server to unwrap the file key first, and falls back to the local key store only when the unwrap request fails due to transport/server unavailability. Online 403/404 unwrap responses must not fall back to local keys.

**Tech Stack:** .NET 10, agent core workflows, WPF tray/viewer shells, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Agent.Core/ProtectPdfFileWorkflow.cs`: call `IDrmServerClient.WrapFileKeyAsync` before producing final output or saving local key fallback.
- Modify `src/Drm.Agent.Core/OpenProtectedPdfFileWorkflow.cs`: call `IDrmServerClient.UnwrapFileKeyAsync` before local key store lookup and constrain fallback to transport failures only.
- Modify `src/Drm.Viewer.Windows/MainWindow.xaml.cs`: pass `JsonPolicyDecisionCache` into open workflow so online opens seed offline access decisions.
- Modify `tests/Drm.Agent.Core.Tests/ProtectPdfFileWorkflowTests.cs`: add key-wrap success and wrap-failure safety tests.
- Modify `tests/Drm.Agent.Core.Tests/OpenProtectedPdfFileWorkflowTests.cs`: add server unwrap primary, online denial no-fallback, and transport fallback tests.
- Modify `README.md`: document the desktop server-key flow.

## Tasks

### Task 1: Protect Workflow Server Wrap

- [x] **Step 1: Write failing tests**

Add tests in `tests/Drm.Agent.Core.Tests/ProtectPdfFileWorkflowTests.cs`:
- `ProtectPdfFileWorkflow_wraps_file_key_with_server`
- `ProtectPdfFileWorkflow_leaves_original_and_no_output_when_key_wrap_fails`

The recording fake must capture `(tenantId, fileId, fileKey)` passed to `WrapFileKeyAsync` and allow a `FailKeyWrap` flag that throws `HttpRequestException("key wrap failed")`.

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ProtectPdfFileWorkflow
```

Expected: the new wrap assertion fails because `ProtectPdfFileWorkflow` does not call `WrapFileKeyAsync`.

- [x] **Step 3: Implement server wrap**

In `src/Drm.Agent.Core/ProtectPdfFileWorkflow.cs`, call:

```csharp
await serverClient.WrapFileKeyAsync(
    tenantId.Value,
    fileId.Value,
    fileKey,
    cancellationToken);
```

immediately after successful `RegisterFileAsync` and before creating/moving the protected output.

- [x] **Step 4: Run passing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter ProtectPdfFileWorkflow
```

Expected: all protect file workflow tests pass.

### Task 2: Open Workflow Server Unwrap

- [x] **Step 1: Write failing tests**

Add tests in `tests/Drm.Agent.Core.Tests/OpenProtectedPdfFileWorkflowTests.cs`:
- `OpenProtectedPdfFileWorkflow_uses_server_unwrap_when_local_key_is_missing`
- `OpenProtectedPdfFileWorkflow_does_not_fallback_to_local_key_when_server_denies_unwrap`
- `OpenProtectedPdfFileWorkflow_falls_back_to_local_key_when_unwrap_transport_fails`

The fake server must store file keys from `WrapFileKeyAsync`, return them from `UnwrapFileKeyAsync`, optionally throw `HttpRequestException` with `HttpStatusCode.Forbidden`, and record unwrap requests.

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter OpenProtectedPdfFileWorkflow
```

Expected: the server-primary test fails because the workflow currently reads the local key store before calling the server unwrap API.

- [x] **Step 3: Implement server-primary unwrap**

In `src/Drm.Agent.Core/OpenProtectedPdfFileWorkflow.cs`:
- Read the protected header before key lookup.
- Call `serverClient.UnwrapFileKeyAsync(..., Permission.View.ToString(), cancellationToken)`.
- If unwrap returns a key, pass it into `OpenProtectedPdfWorkflow`.
- If unwrap throws `HttpRequestException` with `StatusCode` `Forbidden`, throw `UnauthorizedAccessException("Access denied: file_key_denied")`.
- If unwrap throws `HttpRequestException` with `StatusCode` `NotFound`, throw `UnauthorizedAccessException("Access denied: file_key_missing")`.
- If unwrap throws `HttpRequestException` with `StatusCode == null`, load the local key from `IFileKeyStore`; if missing, throw `UnauthorizedAccessException("Access denied: file_key_missing")`; otherwise continue through `OpenProtectedPdfWorkflow`.

- [x] **Step 4: Run passing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter OpenProtectedPdfFileWorkflow
```

Expected: all open file workflow tests pass.

### Task 3: Desktop Wiring, Docs, Verification, Commit

- [x] **Step 1: Wire viewer policy cache**

In `src/Drm.Viewer.Windows/MainWindow.xaml.cs`, instantiate:

```csharp
var decisionCache = new JsonPolicyDecisionCache(ResolveDataPath("policy-decisions.json"));
```

and pass it to `OpenProtectedPdfFileWorkflow(serverClient, keyStore, decisionCache)`.

- [x] **Step 2: Document Phase 3J**

Add a README section explaining that tray protect now stores file keys server-side first and viewer open asks the server for a policy-gated unwrap before falling back to local JSON keys on transport failure.

- [x] **Step 3: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 4: Commit**

Run:

```bash
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3j-desktop-server-key-flow.md
git commit -m "feat: use server key flow in desktop workflows"
```

## Self-Review

- Spec coverage: Moves desktop protect/open key handling from local-primary to server-primary while preserving controlled offline fallback.
- Security note: Online policy/key denial must not be bypassed by local fallback.
- Placeholder scan: No TBD/TODO placeholders.
