# Phase 5N Tamper-Evident Audit Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make newly queued endpoint audit events tamper-evident with a local hash chain.

**Architecture:** Keep the server audit ingestion contract unchanged: `AgentAuditQueue.FlushAsync` still uploads `AgentAuditRecord`. Locally, new queue lines become an envelope containing the audit record, previous entry hash, and current entry hash. Flush verifies the envelope hash before upload, stops at tampered lines, preserves unuploaded suffixes, and still accepts legacy raw `AgentAuditRecord` lines created before this phase.

**Tech Stack:** .NET 10, SHA-256, JSONL queue, xUnit, FluentAssertions.

---

## File Structure

- Modify `tests/Drm.Agent.Core.Tests/AgentAuditQueueTests.cs`: add hash-chain and tamper-stop tests.
- Modify `src/Drm.Agent.Core/AgentAuditQueue.cs`: write and verify hash-chained queue envelopes.
- Modify `README.md`: document Phase 5N.

## Tasks

### Task 1: Hash Chain Tests

- [x] **Step 1: Write failing queue hash tests**

Add tests to `tests/Drm.Agent.Core.Tests/AgentAuditQueueTests.cs`:

```csharp
[Fact]
public async Task Audit_queue_writes_hash_chained_entries()
{
    var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
    var queue = new AgentAuditQueue(path, new RecordingAuditUploader());
    var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);
    await queue.EnqueueAsync(identity, "access_allowed", "allowed", Guid.NewGuid(), CancellationToken.None);

    var lines = File.ReadAllLines(path);
    lines.Should().HaveCount(2);
    using var first = JsonDocument.Parse(lines[0]);
    using var second = JsonDocument.Parse(lines[1]);

    first.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    first.RootElement.GetProperty("previousHash").ValueKind.Should().Be(JsonValueKind.Null);
    first.RootElement.GetProperty("entryHash").GetString().Should().NotBeNullOrWhiteSpace();
    first.RootElement.GetProperty("record").GetProperty("eventType").GetString().Should().Be("agent_heartbeat");

    second.RootElement.GetProperty("previousHash").GetString()
        .Should().Be(first.RootElement.GetProperty("entryHash").GetString());
    second.RootElement.GetProperty("record").GetProperty("eventType").GetString().Should().Be("access_allowed");
}

[Fact]
public async Task Audit_queue_stops_flush_when_hash_chain_entry_is_tampered()
{
    var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
    var uploader = new RecordingAuditUploader();
    var queue = new AgentAuditQueue(path, uploader);
    var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);
    await queue.EnqueueAsync(identity, "access_allowed", "allowed", Guid.NewGuid(), CancellationToken.None);

    var lines = File.ReadAllLines(path);
    lines[1] = lines[1].Replace("access_allowed", "access_denied", StringComparison.Ordinal);
    File.WriteAllLines(path, lines);

    await queue.FlushAsync(CancellationToken.None);

    uploader.UploadedEvents.Should().ContainSingle(record => record.EventType == "agent_heartbeat");
    File.ReadAllLines(path).Should().ContainSingle().Which.Should().Be(lines[1]);
}
```

- [x] **Step 2: Run failing queue hash tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "Audit_queue_writes_hash_chained_entries|Audit_queue_stops_flush_when_hash_chain_entry_is_tampered"
```

Expected: FAIL because queued lines are raw audit records, not envelopes.

### Task 2: Hash Chain Implementation

- [x] **Step 1: Implement queue envelopes**

Update `src/Drm.Agent.Core/AgentAuditQueue.cs`:

- Add an internal `AuditQueueEntry` record with `SchemaVersion`, `PreviousHash`, `Record`, and `EntryHash`.
- On enqueue, read the last nonblank line and use its valid `EntryHash` as `PreviousHash`; if none exists, use null.
- Hash canonical data built from `schemaVersion`, `previousHash`, and serialized `record` using SHA-256 lowercase hex.
- Write one JSON envelope per line.

- [x] **Step 2: Verify on flush**

Flush behavior:

- Parse new envelope lines and verify `EntryHash` before upload.
- Stop at invalid JSON, null records, invalid hash, or broken chain inside the remaining suffix.
- Preserve the tampered/invalid line and all following nonblank lines.
- Upload legacy raw `AgentAuditRecord` lines as before.
- Preserve existing retry behavior when upload fails.

- [x] **Step 3: Run passing queue tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "AgentAuditQueueTests"
```

Expected: PASS.

### Task 3: Documentation and Verification

- [x] **Step 1: Update README**

Add Phase 5N notes for hash-chained local audit queue entries and legacy queue compatibility.

- [x] **Step 2: Run full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
git diff --check
```

Expected: all pass.

- [x] **Step 3: Commit**

Run:

```bash
git add README.md src/Drm.Agent.Core tests/Drm.Agent.Core.Tests docs/superpowers/plans/2026-05-15-phase-5n-tamper-evident-audit-queue.md
git commit -m "feat: add tamper-evident audit queue"
```

## Self-Review

- Spec coverage: Implements the roadmap's tamper-evident local audit queue without changing server ingestion payloads.
- Security note: This is tamper-evident, not tamper-proof; production should add OS-protected storage and signed batches.
- Placeholder scan: No TBD/TODO placeholders.
