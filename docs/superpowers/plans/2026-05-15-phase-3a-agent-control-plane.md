# Phase 3A Agent Control Plane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first usable desktop-agent control plane: device registration, heartbeat reporting, audit ingestion, and a local queued audit uploader for the Windows service.

**Architecture:** The server owns tenant-scoped device records and immutable audit events. The agent core exposes typed client methods plus an `AgentAuditQueue` that buffers events locally as JSONL and flushes them through the server API. The Windows service wires these core pieces together and emits heartbeat/audit signals without implementing file-system enforcement yet.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core SQLite/Postgres model, xUnit, FluentAssertions.

---

## File Structure

- Modify `src/Drm.Server/Entities.cs`: add `AgentDeviceEntity` with tenant/device/user identity, hostname, OS, agent version, status, registration and heartbeat timestamps.
- Modify `src/Drm.Server/AppDbContext.cs`: add `AgentDevices` DbSet and EF model configuration.
- Create `src/Drm.Server/Endpoints/AgentEndpoints.cs`: map `/api/agent/devices/register`, `/api/agent/devices/{deviceId}/heartbeat`, and `/api/agent/audit`.
- Modify `src/Drm.Server/Program.cs`: register agent endpoints.
- Modify `src/Drm.Agent.Core/DrmServerClient.cs`: add typed client calls for device registration, heartbeat, and audit upload.
- Create `src/Drm.Agent.Core/AgentIdentity.cs`: durable identity/config records for agent operations.
- Create `src/Drm.Agent.Core/AgentAuditQueue.cs`: append-only JSONL queue and flush logic.
- Modify `src/Drm.Agent.Service.Windows/Program.cs`: wire `HttpClient`, `IDrmServerClient`, agent identity/config, and audit queue.
- Modify `src/Drm.Agent.Service.Windows/Worker.cs`: register once, heartbeat periodically, enqueue/upload audit events.
- Create `tests/Drm.Server.Tests/AgentApiTests.cs`: server API coverage.
- Create `tests/Drm.Agent.Core.Tests/AgentAuditQueueTests.cs`: local queue coverage.
- Modify `tests/Drm.Agent.Core.Tests/ProtectAndOpenWorkflowTests.cs`: update fake client for the expanded interface.
- Modify `README.md`: document Phase 3A API.

## Tasks

### Task 1: Server Agent Device API

**Files:**
- Modify: `src/Drm.Server/Entities.cs`
- Modify: `src/Drm.Server/AppDbContext.cs`
- Create: `src/Drm.Server/Endpoints/AgentEndpoints.cs`
- Modify: `src/Drm.Server/Program.cs`
- Test: `tests/Drm.Server.Tests/AgentApiTests.cs`

- [x] **Step 1: Write failing tests**

Add tests that assert:

```csharp
[Fact]
public async Task Agent_can_register_device_and_registration_is_audited()
{
    using var client = factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var deviceId = Guid.NewGuid();

    using var response = await client.PostAsJsonAsync("/api/agent/devices/register", new
    {
        tenantId,
        userId,
        deviceId,
        hostname = "WIN-001",
        operatingSystem = "Windows 11",
        agentVersion = "0.1.0"
    });

    response.StatusCode.Should().Be(HttpStatusCode.Created);

    using var scope = factory.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var device = await dbContext.AgentDevices.SingleAsync();
    device.TenantId.Should().Be(tenantId);
    device.UserId.Should().Be(userId);
    device.DeviceId.Should().Be(deviceId);
    device.Hostname.Should().Be("WIN-001");

    var audit = await dbContext.AuditEvents.SingleAsync();
    audit.EventType.Should().Be("agent_registered");
    audit.ReasonCode.Should().Be("registered");
}
```

Also add tests for heartbeat updating `LastHeartbeatAtUtc`/status, unknown device heartbeat returning 404, and audit ingestion writing an `agent_audit` event.

- [x] **Step 2: Run test to verify it fails**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AgentApiTests`

Expected: compile failure because `AgentDevices` and endpoints do not exist.

- [x] **Step 3: Implement minimal server API**

Create `AgentDeviceEntity`, configure EF keys/indexes, and implement endpoints:

```csharp
POST /api/agent/devices/register
POST /api/agent/devices/{deviceId:guid}/heartbeat
POST /api/agent/audit
```

Validation rules:
- registration requires non-empty hostname, OS, and agent version.
- duplicate registration updates existing metadata and returns 200.
- heartbeat requires existing tenant/device pair.
- audit ingestion only accepts event types starting with `agent_`, `file_`, `access_`, `print_`, `export_`, or `copy_`; invalid event types return 400.

- [x] **Step 4: Run test to verify it passes**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter AgentApiTests`

Expected: PASS.

- [x] **Step 5: Commit**

Run:

```bash
git add src/Drm.Server tests/Drm.Server.Tests/AgentApiTests.cs
git commit -m "feat: add agent device APIs"
```

### Task 2: Agent Core Client and Audit Queue

**Files:**
- Modify: `src/Drm.Agent.Core/DrmServerClient.cs`
- Create: `src/Drm.Agent.Core/AgentIdentity.cs`
- Create: `src/Drm.Agent.Core/AgentAuditQueue.cs`
- Test: `tests/Drm.Agent.Core.Tests/AgentAuditQueueTests.cs`
- Modify: `tests/Drm.Agent.Core.Tests/ProtectAndOpenWorkflowTests.cs`

- [x] **Step 1: Write failing tests**

Add client tests that capture requests and assert:

```csharp
await client.RegisterDeviceAsync(identity, "WIN-001", "Windows 11", "0.1.0", CancellationToken.None);
capturedRequest!.RequestUri.Should().Be(new Uri("https://drm.example/api/agent/devices/register"));
```

Add queue tests:

```csharp
[Fact]
public async Task Audit_queue_keeps_failed_events_and_removes_uploaded_events()
{
    var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
    var uploader = new RecordingAuditUploader(failFirstUpload: true);
    var queue = new AgentAuditQueue(path, uploader);
    var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    await queue.EnqueueAsync(identity, "agent_heartbeat", "online", null, CancellationToken.None);
    await queue.FlushAsync(CancellationToken.None);
    File.ReadAllLines(path).Should().HaveCount(1);

    await queue.FlushAsync(CancellationToken.None);
    File.Exists(path).Should().BeFalse();
    uploader.UploadedEvents.Should().ContainSingle();
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter Agent`

Expected: compile failure because agent identity/client methods/queue do not exist.

- [x] **Step 3: Implement minimal agent core**

Add:
- `AgentIdentity(Guid TenantId, Guid UserId, Guid DeviceId)`
- `AgentAuditRecord` with tenant, user, device, file, type, reason, created timestamp.
- `IAgentAuditUploader` abstraction implemented by `DrmServerClient`.
- `AgentAuditQueue` using JSONL append, temp-file rewrite on successful flush, and sequential upload so failed events remain on disk.

- [x] **Step 4: Run test to verify it passes**

Run: `/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter Agent`

Expected: PASS.

- [x] **Step 5: Commit**

Run:

```bash
git add src/Drm.Agent.Core tests/Drm.Agent.Core.Tests
git commit -m "feat: add agent audit queue"
```

### Task 3: Windows Service Wiring and Docs

**Files:**
- Modify: `src/Drm.Agent.Service.Windows/Program.cs`
- Modify: `src/Drm.Agent.Service.Windows/Worker.cs`
- Modify: `README.md`

- [x] **Step 1: Write build-facing implementation**

Wire configuration keys:

```json
{
  "DrmAgent": {
    "ServerUrl": "https://drm.example",
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "UserId": "00000000-0000-0000-0000-000000000000",
    "DeviceId": "00000000-0000-0000-0000-000000000000",
    "AuditQueuePath": "%ProgramData%\\DRM\\agent-audit.jsonl",
    "HeartbeatIntervalSeconds": 60
  }
}
```

The worker should register the device at startup, record an online heartbeat, flush any queued audit events, then sleep for the configured interval.

- [x] **Step 2: Build service to verify wiring**

Run: `/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj`

Expected: PASS.

- [x] **Step 3: Document APIs and configuration**

Update README with:
- device registration endpoint
- heartbeat endpoint
- audit ingestion endpoint
- Windows service configuration block
- note that this is a visible managed agent, not stealth endpoint software

- [x] **Step 4: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```

Expected: all pass.

- [x] **Step 5: Commit**

Run:

```bash
git add src/Drm.Agent.Service.Windows README.md
git commit -m "docs: document agent control plane"
```

## Self-Review

- Spec coverage: Covers Windows background service responsibilities for device registration, health reporting, and audit buffering/upload. Does not yet implement watcher auto-encryption, revocation command polling, or offline leases; those remain separate Phase 3 tasks.
- Placeholder scan: No TBD/TODO placeholders.
- Type consistency: `AgentIdentity`, `AgentAuditRecord`, and endpoint request names are used consistently across tasks.
