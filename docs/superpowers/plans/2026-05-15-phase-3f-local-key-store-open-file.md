# Phase 3F Local Key Store and File Open Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local key-store abstraction and file-based open workflow so desktop entry points can open protected files without passing raw keys through UI code.

**Architecture:** Keep the existing `OpenProtectedPdfWorkflow` as the policy/decrypt primitive. Add `IFileKeyStore` with a JSON implementation for development/local MVP use, store a generated file key when protecting via `ProtectPdfFileWorkflow`, and add `OpenProtectedPdfFileWorkflow` that reads `.drmx`, loads the key by tenant/file ID, then delegates to the existing open workflow. This is a development bridge until server-side key wrapping/KMS is implemented.

**Tech Stack:** .NET 10, existing DRM container format, JSON local store, xUnit, FluentAssertions.

---

## File Structure

- Create `src/Drm.Agent.Core/FileKeyStore.cs`: key-store records, interface, JSON implementation.
- Modify `src/Drm.Agent.Core/ProtectPdfFileWorkflow.cs`: optionally persist the file key after output verification.
- Create `src/Drm.Agent.Core/OpenProtectedPdfFileWorkflow.cs`: file-based open using inventory/key store/core open workflow.
- Create `tests/Drm.Agent.Core.Tests/FileKeyStoreTests.cs`: save/load/missing key behavior.
- Create `tests/Drm.Agent.Core.Tests/OpenProtectedPdfFileWorkflowTests.cs`: end-to-end protect file then open file with key store.
- Modify `README.md`: document that JSON key store is a local MVP bridge, not production KMS.

## Tasks

### Task 1: Key Store

- [x] **Step 1: Write failing tests**

Add tests for:
- saving and loading a key by tenant/file ID;
- returning null for a missing key;
- replacing an existing key for the same tenant/file ID.

- [x] **Step 2: Run failing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter FileKeyStore`

Expected: compile failure because key-store types do not exist.

- [x] **Step 3: Implement key store**

Create `IFileKeyStore`, `FileKeyRecord`, and `JsonFileKeyStore`. Store base64 keys in JSON using atomic temp-file rewrite.

- [x] **Step 4: Run passing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter FileKeyStore`

Expected: PASS.

### Task 2: File-Based Open Workflow

- [x] **Step 1: Write failing tests**

Add an end-to-end test:
- protect a source PDF via `ProtectPdfFileWorkflow` with a key store;
- open the resulting `.drmx` using `OpenProtectedPdfFileWorkflow`;
- assert decrypted content and watermark;
- assert missing key fails with `file_key_missing`.

- [x] **Step 2: Run failing test**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter OpenProtectedPdfFileWorkflow`

Expected: compile failure because file-open workflow/key-store constructor path does not exist.

- [x] **Step 3: Implement workflow and key-store integration**

Update `ProtectPdfFileWorkflow` with an optional `IFileKeyStore` constructor parameter and store the key after output verification and before inventory upsert. Add `OpenProtectedPdfFileWorkflow` that parses the container header, loads the file key, and delegates to `OpenProtectedPdfWorkflow`.

- [x] **Step 4: Run passing tests**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "FileKeyStore|OpenProtectedPdfFileWorkflow"`

Expected: PASS.

### Task 3: Docs, Verification, Commit

- [x] **Step 1: Document local key store**

Update README with local key-store behavior and production warning.

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
git add src tests README.md docs/superpowers/plans/2026-05-15-phase-3f-local-key-store-open-file.md
git commit -m "feat: add local file key store"
```

## Self-Review

- Spec coverage: Supports file open workflow for desktop UI and provides a bridge toward key unwrap authorization.
- Security note: JSON key store is explicitly local MVP/development only and must be replaced by server-side key wrapping/KMS before production.
- Placeholder scan: No TBD/TODO placeholders.
