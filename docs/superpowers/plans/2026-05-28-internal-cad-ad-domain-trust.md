# Internal CAD AD Domain Trust Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change the demo/product flow from external password/share-link DRM to internal-only CAD file encryption gated by on-prem Active Directory domain-joined Windows devices and AD login posture.

**Architecture:** Keep the existing `.drmx` encryption, file key wrapping, grant, audit, and device trust pipeline. Extend device trust with AD posture captured by the Windows agent (`domainJoined`, `domainName`, `windowsUser`) and enforced server-side before policy grants are evaluated. Narrow the primary Windows tray flow to "Protect CAD file" and stop creating external share links from that flow.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core SQLite/Postgres compatibility, WPF Windows tray app, Windows `Netapi32.dll` P/Invoke for on-prem domain join detection, xUnit/FluentAssertions.

---

## File Structure

- `src/Drm.Server/Entities.cs`
  - Extend `AgentDeviceEntity` with AD posture fields.
  - Extend `TenantDeviceTrustConfigEntity` with AD enforcement settings.
- `src/Drm.Server/AppDbContext.cs`
  - Configure lengths/default persistence for new string fields.
- `src/Drm.Server/Program.cs`
  - Add SQLite/Postgres migration guards for new columns on existing DBs.
- `src/Drm.Server/Endpoints/AgentEndpoints.cs`
  - Accept AD posture in device register and heartbeat requests.
  - Persist the posture on `AgentDeviceEntity`.
- `src/Drm.Server/Endpoints/AdminDeviceTrustEndpoints.cs`
  - Expose AD trust settings through the existing tenant device trust endpoint.
- `src/Drm.Server/PolicyDecisionService.cs`
  - Enforce domain-joined device and allowed AD domain before normal grant checks.
- `tests/Drm.Server.Tests/V19FeatureTests.cs`
  - Add red/green integration tests for AD trust settings and policy denies.
- `src/Drm.Agent.Core/AgentIdentity.cs`
  - Add `AgentDevicePosture` value object.
- `src/Drm.Agent.Core/DrmServerClient.cs`
  - Send posture to register/heartbeat endpoints while preserving old method compatibility.
- `src/Drm.Agent.Core/AgentHeartbeatWorkflow.cs`
  - Accept optional posture and forward it to the server.
- `src/Drm.Agent.Core/WindowsDomainPosture.cs`
  - New focused AD posture provider using `NetGetJoinInformation`.
- `tests/Drm.Agent.Core.Tests/AgentHeartbeatWorkflowTests.cs`
  - Add coverage that heartbeat forwards posture.
- `src/Drm.Agent.Service.Windows/Worker.cs`
  - Capture AD posture before heartbeat.
- `src/Drm.Agent.Tray.Windows/MainWindow.xaml`
  - Reword primary panel to internal CAD protect and remove recipient-centric copy.
- `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`
  - Validate CAD extensions and protect locally without creating share links or opening email.
- `src/Drm.Agent.Shell.Windows/install.ps1`
  - Rename Explorer shell label from Quick Send to Protect CAD.
- `src/Drm.Agent.Shell.Windows/status.ps1`
  - Keep status checks aligned with shell verb label.
- `docs/demo/*.md`, `README.md`
  - Update demo story to internal CAD + AD trust.

---

### Task 1: Server Tests for AD Device Trust Contract

**Files:**
- Modify: `tests/Drm.Server.Tests/V19FeatureTests.cs`

- [ ] **Step 1: Write failing tests for AD device trust settings and denials**

Add these tests inside the "Device trust enforcement" section, after `PutDeviceTrust_upserts_config`:

```csharp
[Fact]
public async Task PutDeviceTrust_accepts_ad_domain_requirements()
{
    var (tenantId, _) = await SeedTenantUserAsync();
    using var client = AdminClient();
    client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

    var res = await client.PutAsJsonAsync(
        $"/api/admin/tenants/{tenantId}/device-trust",
        new
        {
            enabled = true,
            requiredCheckinDays = 3,
            requireDomainJoined = true,
            allowedAdDomains = new[] { "CORP", "ENGINEERING" }
        });

    res.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await res.Content.ReadFromJsonAsync<DeviceTrustRow>();
    body!.Enabled.Should().BeTrue();
    body.RequireDomainJoined.Should().BeTrue();
    body.AllowedAdDomains.Should().BeEquivalentTo("CORP", "ENGINEERING");
}

[Fact]
public async Task DeviceTrust_denies_access_when_device_is_not_domain_joined()
{
    var (tenantId, userId) = await SeedTenantUserAsync();
    var deviceId = Guid.NewGuid();
    var ownerId = Guid.NewGuid();
    var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

    await SeedDeviceAsync(
        tenantId,
        deviceId,
        userId,
        lastHeartbeat: DateTimeOffset.UtcNow,
        domainJoined: false,
        domainName: "",
        windowsUser: "WORKGROUP\\alice");

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
    {
        TenantId = tenantId,
        Enabled = true,
        RequiredCheckinDays = 7,
        RequireDomainJoined = true,
        AllowedAdDomainsCsv = "CORP",
        UpdatedAtUtc = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
    result!.Allowed.Should().BeFalse();
    result.ReasonCode.Should().Be("device_not_domain_joined");
}

[Fact]
public async Task DeviceTrust_denies_access_when_domain_is_not_allowed()
{
    var (tenantId, userId) = await SeedTenantUserAsync();
    var deviceId = Guid.NewGuid();
    var ownerId = Guid.NewGuid();
    var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

    await SeedDeviceAsync(
        tenantId,
        deviceId,
        userId,
        lastHeartbeat: DateTimeOffset.UtcNow,
        domainJoined: true,
        domainName: "VENDOR",
        windowsUser: "VENDOR\\alice");

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
    {
        TenantId = tenantId,
        Enabled = true,
        RequiredCheckinDays = 7,
        RequireDomainJoined = true,
        AllowedAdDomainsCsv = "CORP",
        UpdatedAtUtc = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
    result!.Allowed.Should().BeFalse();
    result.ReasonCode.Should().Be("ad_domain_not_allowed");
}

[Fact]
public async Task DeviceTrust_allows_access_when_domain_joined_and_domain_allowed()
{
    var (tenantId, userId) = await SeedTenantUserAsync();
    var deviceId = Guid.NewGuid();
    var ownerId = Guid.NewGuid();
    var fileId = await SeedFileWithGrantAsync(tenantId, ownerId, userId);

    await SeedDeviceAsync(
        tenantId,
        deviceId,
        userId,
        lastHeartbeat: DateTimeOffset.UtcNow,
        domainJoined: true,
        domainName: "CORP",
        windowsUser: "CORP\\alice");

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.TenantDeviceTrustConfigs.Add(new TenantDeviceTrustConfigEntity
    {
        TenantId = tenantId,
        Enabled = true,
        RequiredCheckinDays = 7,
        RequireDomainJoined = true,
        AllowedAdDomainsCsv = "CORP,ENGINEERING",
        UpdatedAtUtc = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var result = await SimulateAsync(tenantId, fileId, userId, deviceId);
    result!.Allowed.Should().BeTrue();
}
```

- [ ] **Step 2: Update test helper signatures and DTOs**

Replace `SeedDeviceAsync` with this version:

```csharp
private async Task SeedDeviceAsync(
    Guid tenantId,
    Guid deviceId,
    Guid userId,
    DateTimeOffset? lastHeartbeat = null,
    bool domainJoined = false,
    string domainName = "",
    string windowsUser = "")
{
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.AgentDevices.Add(new AgentDeviceEntity
    {
        TenantId = tenantId,
        DeviceId = deviceId,
        UserId = userId,
        Hostname = "test-host",
        OperatingSystem = "Windows",
        AgentVersion = "1.0",
        Status = "active",
        RegisteredAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        LastHeartbeatAtUtc = lastHeartbeat,
        DomainJoined = domainJoined,
        DomainName = domainName,
        WindowsUser = windowsUser
    });
    await db.SaveChangesAsync();
}
```

Replace `DeviceTrustRow` with:

```csharp
private sealed record DeviceTrustRow(
    Guid TenantId,
    bool Enabled,
    int RequiredCheckinDays,
    bool RequireDomainJoined,
    IReadOnlyList<string> AllowedAdDomains,
    DateTimeOffset? UpdatedAtUtc);
```

- [ ] **Step 3: Run tests to verify RED**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "FullyQualifiedName~V19FeatureTests"
```

Expected:

```text
CS0117: 'AgentDeviceEntity' does not contain a definition for 'DomainJoined'
CS0117: 'TenantDeviceTrustConfigEntity' does not contain a definition for 'RequireDomainJoined'
```

If the project compiles, the new tests should fail because the API response does not include `requireDomainJoined` and policy does not deny for AD posture yet.

---

### Task 2: Server Schema and API for AD Posture

**Files:**
- Modify: `src/Drm.Server/Entities.cs`
- Modify: `src/Drm.Server/AppDbContext.cs`
- Modify: `src/Drm.Server/Program.cs`
- Modify: `src/Drm.Server/Endpoints/AgentEndpoints.cs`
- Modify: `src/Drm.Server/Endpoints/AdminDeviceTrustEndpoints.cs`

- [ ] **Step 1: Add entity properties**

In `AgentDeviceEntity`, add:

```csharp
public bool DomainJoined { get; set; }

public string DomainName { get; set; } = string.Empty;

public string WindowsUser { get; set; } = string.Empty;
```

In `TenantDeviceTrustConfigEntity`, add:

```csharp
/// <summary>When true, access requires the endpoint to report an on-prem AD domain join.</summary>
public bool RequireDomainJoined { get; set; }

/// <summary>Comma-separated NetBIOS or DNS AD domain names accepted for trusted devices.</summary>
public string AllowedAdDomainsCsv { get; set; } = string.Empty;
```

- [ ] **Step 2: Configure EF model lengths**

In `AppDbContext.cs`, inside `AgentDeviceEntity` configuration:

```csharp
entity.Property(device => device.DomainName).HasMaxLength(256);
entity.Property(device => device.WindowsUser).HasMaxLength(256);
```

Inside `TenantDeviceTrustConfigEntity` configuration:

```csharp
entity.Property(c => c.AllowedAdDomainsCsv).HasMaxLength(1024);
```

- [ ] **Step 3: Add SQLite migration guards**

In the SQLite branch of `Program.cs`, after the `TenantDeviceTrustConfigs` `CREATE TABLE IF NOT EXISTS` block, add:

```csharp
var deviceTrustColumns = new HashSet<string>(StringComparer.Ordinal);
var deviceTrustConn = dbContext.Database.GetDbConnection();
if (deviceTrustConn.State != System.Data.ConnectionState.Open) deviceTrustConn.Open();
using (var command = deviceTrustConn.CreateCommand())
{
    command.CommandText = "PRAGMA table_info(\"TenantDeviceTrustConfigs\");";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        deviceTrustColumns.Add(reader.GetString(1));
    }
}
if (!deviceTrustColumns.Contains("RequireDomainJoined"))
    dbContext.Database.ExecuteSqlRaw("""ALTER TABLE "TenantDeviceTrustConfigs" ADD COLUMN "RequireDomainJoined" INTEGER NOT NULL DEFAULT 0;""");
if (!deviceTrustColumns.Contains("AllowedAdDomainsCsv"))
    dbContext.Database.ExecuteSqlRaw("""ALTER TABLE "TenantDeviceTrustConfigs" ADD COLUMN "AllowedAdDomainsCsv" TEXT NOT NULL DEFAULT '';""");

var agentDeviceColumns = new HashSet<string>(StringComparer.Ordinal);
using (var command = deviceTrustConn.CreateCommand())
{
    command.CommandText = "PRAGMA table_info(\"AgentDevices\");";
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        agentDeviceColumns.Add(reader.GetString(1));
    }
}
if (!agentDeviceColumns.Contains("DomainJoined"))
    dbContext.Database.ExecuteSqlRaw("""ALTER TABLE "AgentDevices" ADD COLUMN "DomainJoined" INTEGER NOT NULL DEFAULT 0;""");
if (!agentDeviceColumns.Contains("DomainName"))
    dbContext.Database.ExecuteSqlRaw("""ALTER TABLE "AgentDevices" ADD COLUMN "DomainName" TEXT NOT NULL DEFAULT '';""");
if (!agentDeviceColumns.Contains("WindowsUser"))
    dbContext.Database.ExecuteSqlRaw("""ALTER TABLE "AgentDevices" ADD COLUMN "WindowsUser" TEXT NOT NULL DEFAULT '';""");
```

- [ ] **Step 4: Add Postgres migration guards**

In the Postgres branch of `Program.cs`, after the `TenantDeviceTrustConfigs` `CREATE TABLE IF NOT EXISTS` block, add:

```csharp
dbContext.Database.ExecuteSqlRaw("""
    ALTER TABLE "TenantDeviceTrustConfigs" ADD COLUMN IF NOT EXISTS "RequireDomainJoined" boolean NOT NULL DEFAULT FALSE;
    """);
dbContext.Database.ExecuteSqlRaw("""
    ALTER TABLE "TenantDeviceTrustConfigs" ADD COLUMN IF NOT EXISTS "AllowedAdDomainsCsv" text NOT NULL DEFAULT '';
    """);
dbContext.Database.ExecuteSqlRaw("""
    ALTER TABLE "AgentDevices" ADD COLUMN IF NOT EXISTS "DomainJoined" boolean NOT NULL DEFAULT FALSE;
    """);
dbContext.Database.ExecuteSqlRaw("""
    ALTER TABLE "AgentDevices" ADD COLUMN IF NOT EXISTS "DomainName" text NOT NULL DEFAULT '';
    """);
dbContext.Database.ExecuteSqlRaw("""
    ALTER TABLE "AgentDevices" ADD COLUMN IF NOT EXISTS "WindowsUser" text NOT NULL DEFAULT '';
    """);
```

- [ ] **Step 5: Persist AD posture from agent register and heartbeat**

In `AgentEndpoints.cs`, update `RegisterDeviceRequest`:

```csharp
private sealed record RegisterDeviceRequest(
    Guid TenantId,
    Guid UserId,
    Guid DeviceId,
    string Hostname,
    string OperatingSystem,
    string AgentVersion,
    bool? DomainJoined,
    string? DomainName,
    string? WindowsUser);
```

Update `HeartbeatRequest`:

```csharp
private sealed record HeartbeatRequest(
    Guid TenantId,
    Guid UserId,
    string Status,
    string AgentVersion,
    bool? DomainJoined,
    string? DomainName,
    string? WindowsUser);
```

Add this helper near `IsBlank`:

```csharp
private static void ApplyPosture(
    AgentDeviceEntity device,
    bool? domainJoined,
    string? domainName,
    string? windowsUser)
{
    if (domainJoined.HasValue)
    {
        device.DomainJoined = domainJoined.Value;
    }

    if (domainName is not null)
    {
        device.DomainName = domainName.Trim();
    }

    if (windowsUser is not null)
    {
        device.WindowsUser = windowsUser.Trim();
    }
}
```

Call it before saving both existing and new devices:

```csharp
ApplyPosture(device, request.DomainJoined, request.DomainName, request.WindowsUser);
```

- [ ] **Step 6: Expose AD trust settings through admin endpoint**

In `AdminDeviceTrustEndpoints.cs`, replace request and response records:

```csharp
private sealed record UpsertDeviceTrustRequest(
    bool Enabled,
    int RequiredCheckinDays,
    bool RequireDomainJoined,
    IReadOnlyList<string>? AllowedAdDomains);

private sealed record DeviceTrustResponse(
    Guid TenantId,
    bool Enabled,
    int RequiredCheckinDays,
    bool RequireDomainJoined,
    IReadOnlyList<string> AllowedAdDomains,
    DateTimeOffset? UpdatedAtUtc)
{
    public static DeviceTrustResponse From(TenantDeviceTrustConfigEntity? c, Guid tenantId)
        => c is null
            ? new(tenantId, false, 7, false, [], null)
            : new(
                tenantId,
                c.Enabled,
                c.RequiredCheckinDays,
                c.RequireDomainJoined,
                ParseDomains(c.AllowedAdDomainsCsv),
                c.UpdatedAtUtc);
}
```

Add helpers:

```csharp
private static IReadOnlyList<string> NormalizeDomains(IEnumerable<string>? domains)
    => domains?
        .Select(domain => domain.Trim().ToUpperInvariant())
        .Where(domain => domain.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
        .ToList()
        ?? [];

private static IReadOnlyList<string> ParseDomains(string? csv)
    => NormalizeDomains((csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
```

Inside `UpsertAsync`, before save:

```csharp
var domains = NormalizeDomains(request.AllowedAdDomains);
config.RequireDomainJoined = request.RequireDomainJoined;
config.AllowedAdDomainsCsv = string.Join(",", domains);
```

- [ ] **Step 7: Run tests to verify server schema/API GREEN for compile**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "PutDeviceTrust_accepts_ad_domain_requirements"
```

Expected:

```text
Passed!  - Failed: 0
```

The policy denial tests may still fail until Task 3.

---

### Task 3: Server Policy Enforcement for AD Domain Trust

**Files:**
- Modify: `src/Drm.Server/PolicyDecisionService.cs`
- Test: `tests/Drm.Server.Tests/V19FeatureTests.cs`

- [ ] **Step 1: Add shared denial helper in policy service**

Inside `PolicyDecisionService`, before `DecideInternalAsync`, add:

```csharp
private async Task<ServerPolicyDecision> DenyWithAuditAsync(
    Guid tenantId,
    Guid fileId,
    Guid userId,
    string reasonCode,
    DateTimeOffset decisionTime,
    bool fileFound,
    bool writeAudit,
    CancellationToken cancellationToken)
{
    if (writeAudit)
    {
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            EventType = "access_denied",
            ReasonCode = reasonCode,
            CreatedAtUtc = decisionTime
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await notificationService.NotifyAsync(
            tenantId,
            new AdminNotificationEvent(
                "access_denied",
                fileId,
                userId,
                null,
                decisionTime,
                ReasonCode: reasonCode),
            cancellationToken);
    }

    return new ServerPolicyDecision(
        false,
        Permission.None,
        reasonCode,
        null,
        null,
        FileFound: fileFound,
        InvalidPermission: false);
}
```

- [ ] **Step 2: Replace existing repeated device denial blocks where practical**

For `device_disabled`, keep behavior the same but call:

```csharp
return await DenyWithAuditAsync(
    tenantId,
    fileId,
    userId,
    "device_disabled",
    decisionTime,
    fileFound: true,
    writeAudit,
    cancellationToken);
```

This keeps the next AD trust checks small and readable.

- [ ] **Step 3: Enforce domain joined and allowed domain**

Replace the current `// v1.9: device trust enforcement` block with:

```csharp
// v1.9/v1.10: device trust and on-prem AD posture enforcement
if (deviceId != Guid.Empty)
{
    var trustConfig = await dbContext.TenantDeviceTrustConfigs
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

    if (trustConfig is { Enabled: true })
    {
        var device = await dbContext.AgentDevices
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.DeviceId == deviceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (trustConfig.RequireDomainJoined)
        {
            if (device is null || !device.DomainJoined)
            {
                return await DenyWithAuditAsync(
                    tenantId,
                    fileId,
                    userId,
                    "device_not_domain_joined",
                    decisionTime,
                    fileFound: true,
                    writeAudit,
                    cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(device.WindowsUser))
            {
                return await DenyWithAuditAsync(
                    tenantId,
                    fileId,
                    userId,
                    "ad_user_not_detected",
                    decisionTime,
                    fileFound: true,
                    writeAudit,
                    cancellationToken);
            }

            var allowedDomains = ParseAllowedDomains(trustConfig.AllowedAdDomainsCsv);
            if (allowedDomains.Count > 0 &&
                !allowedDomains.Contains(NormalizeDomain(device.DomainName), StringComparer.OrdinalIgnoreCase))
            {
                return await DenyWithAuditAsync(
                    tenantId,
                    fileId,
                    userId,
                    "ad_domain_not_allowed",
                    decisionTime,
                    fileFound: true,
                    writeAudit,
                    cancellationToken);
            }
        }

        var cutoff = decisionTime.AddDays(-trustConfig.RequiredCheckinDays);
        var lastCheckin = device?.LastHeartbeatAtUtc;

        if (lastCheckin == null || lastCheckin.Value < cutoff)
        {
            return await DenyWithAuditAsync(
                tenantId,
                fileId,
                userId,
                "device_trust_expired",
                decisionTime,
                fileFound: true,
                writeAudit,
                cancellationToken);
        }
    }
}
```

Add helpers at class scope:

```csharp
private static IReadOnlySet<string> ParseAllowedDomains(string? csv)
    => (csv ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeDomain)
        .Where(domain => domain.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

private static string NormalizeDomain(string? domain)
    => (domain ?? string.Empty).Trim().ToUpperInvariant();
```

- [ ] **Step 4: Run focused server tests**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "FullyQualifiedName~V19FeatureTests"
```

Expected:

```text
Passed!  - Failed: 0
```

---

### Task 4: Agent Core AD Posture Model and Client Wire Format

**Files:**
- Modify: `src/Drm.Agent.Core/AgentIdentity.cs`
- Modify: `src/Drm.Agent.Core/DrmServerClient.cs`
- Modify: `src/Drm.Agent.Core/AgentHeartbeatWorkflow.cs`
- Modify: `tests/Drm.Agent.Core.Tests/AgentHeartbeatWorkflowTests.cs`

- [ ] **Step 1: Write failing test that heartbeat forwards posture**

In `AgentHeartbeatWorkflowTests.cs`, add:

```csharp
[Fact]
public async Task ReportOnlineAsync_forwards_device_posture()
{
    var identity = new AgentIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    var server = new RecordingServerClient();
    var auditQueue = new RecordingAuditQueue();
    var workflow = new AgentHeartbeatWorkflow(server, auditQueue);
    var posture = new AgentDevicePosture(
        DomainJoined: true,
        DomainName: "CORP",
        WindowsUser: "CORP\\alice");

    await workflow.ReportOnlineAsync(
        identity,
        "cad-ws-01",
        "Windows 11",
        "1.6.1",
        posture,
        CancellationToken.None);

    server.RegisteredPosture.Should().Be(posture);
    server.HeartbeatPosture.Should().Be(posture);
}
```

Extend the test fake with:

```csharp
public AgentDevicePosture? RegisteredPosture { get; private set; }
public AgentDevicePosture? HeartbeatPosture { get; private set; }

public Task<AgentDeviceRegistration> RegisterDeviceAsync(
    AgentIdentity identity,
    string hostname,
    string operatingSystem,
    string agentVersion,
    AgentDevicePosture posture,
    CancellationToken cancellationToken)
{
    RegisteredPosture = posture;
    return RegisterDeviceAsync(identity, hostname, operatingSystem, agentVersion, cancellationToken);
}

public Task<AgentHeartbeat> RecordHeartbeatAsync(
    AgentIdentity identity,
    string status,
    string agentVersion,
    AgentDevicePosture posture,
    CancellationToken cancellationToken)
{
    HeartbeatPosture = posture;
    return RecordHeartbeatAsync(identity, status, agentVersion, cancellationToken);
}
```

- [ ] **Step 2: Run Agent Core test to verify RED**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "ReportOnlineAsync_forwards_device_posture"
```

Expected:

```text
CS0246: The type or namespace name 'AgentDevicePosture' could not be found
```

- [ ] **Step 3: Add posture value object and compatible interface overloads**

In `AgentIdentity.cs`, add:

```csharp
public sealed record AgentDevicePosture(
    bool DomainJoined,
    string DomainName,
    string WindowsUser)
{
    public static AgentDevicePosture Unknown { get; } = new(false, string.Empty, string.Empty);
}
```

In `IDrmServerClient`, add default overloads without breaking existing test doubles:

```csharp
Task<AgentDeviceRegistration> RegisterDeviceAsync(
    AgentIdentity identity,
    string hostname,
    string operatingSystem,
    string agentVersion,
    AgentDevicePosture posture,
    CancellationToken cancellationToken)
    => RegisterDeviceAsync(identity, hostname, operatingSystem, agentVersion, cancellationToken);

Task<AgentHeartbeat> RecordHeartbeatAsync(
    AgentIdentity identity,
    string status,
    string agentVersion,
    AgentDevicePosture posture,
    CancellationToken cancellationToken)
    => RecordHeartbeatAsync(identity, status, agentVersion, cancellationToken);
```

- [ ] **Step 4: Update `AgentHeartbeatWorkflow`**

Replace the method with overload-preserving implementation:

```csharp
public async Task ReportOnlineAsync(
    AgentIdentity identity,
    string hostname,
    string operatingSystem,
    string agentVersion,
    CancellationToken cancellationToken)
{
    await ReportOnlineAsync(
        identity,
        hostname,
        operatingSystem,
        agentVersion,
        AgentDevicePosture.Unknown,
        cancellationToken);
}

public async Task ReportOnlineAsync(
    AgentIdentity identity,
    string hostname,
    string operatingSystem,
    string agentVersion,
    AgentDevicePosture posture,
    CancellationToken cancellationToken)
{
    await serverClient.RegisterDeviceAsync(
        identity,
        hostname,
        operatingSystem,
        agentVersion,
        posture,
        cancellationToken);

    await serverClient.RecordHeartbeatAsync(
        identity,
        "online",
        agentVersion,
        posture,
        cancellationToken);

    await auditQueue.FlushAsync(cancellationToken);
}
```

- [ ] **Step 5: Update `DrmServerClient` wire payload**

Add overloads:

```csharp
public async Task<AgentDeviceRegistration> RegisterDeviceAsync(
    AgentIdentity identity,
    string hostname,
    string operatingSystem,
    string agentVersion,
    AgentDevicePosture posture,
    CancellationToken cancellationToken)
{
    var response = await httpClient.PostAsJsonAsync(
        "/api/agent/devices/register",
        new RegisterDeviceRequest(
            identity.TenantId,
            identity.UserId,
            identity.DeviceId,
            hostname,
            operatingSystem,
            agentVersion,
            posture.DomainJoined,
            posture.DomainName,
            posture.WindowsUser),
        JsonOptions,
        cancellationToken);

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<AgentDeviceRegistration>(JsonOptions, cancellationToken)
        ?? throw new InvalidOperationException("Agent registration response was empty.");
}

public async Task<AgentHeartbeat> RecordHeartbeatAsync(
    AgentIdentity identity,
    string status,
    string agentVersion,
    AgentDevicePosture posture,
    CancellationToken cancellationToken)
{
    var response = await httpClient.PostAsJsonAsync(
        $"/api/agent/devices/{identity.DeviceId}/heartbeat",
        new HeartbeatRequest(
            identity.TenantId,
            identity.UserId,
            status,
            agentVersion,
            posture.DomainJoined,
            posture.DomainName,
            posture.WindowsUser),
        JsonOptions,
        cancellationToken);

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<AgentHeartbeat>(JsonOptions, cancellationToken)
        ?? throw new InvalidOperationException("Agent heartbeat response was empty.");
}
```

Update private request records at the bottom of `DrmServerClient.cs` to include posture fields:

```csharp
private sealed record RegisterDeviceRequest(
    Guid TenantId,
    Guid UserId,
    Guid DeviceId,
    string Hostname,
    string OperatingSystem,
    string AgentVersion,
    bool DomainJoined,
    string DomainName,
    string WindowsUser);

private sealed record HeartbeatRequest(
    Guid TenantId,
    Guid UserId,
    string Status,
    string AgentVersion,
    bool DomainJoined,
    string DomainName,
    string WindowsUser);
```

Keep the old methods by delegating to the new overload with `AgentDevicePosture.Unknown`.

- [ ] **Step 6: Run Agent Core test**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "ReportOnlineAsync_forwards_device_posture"
```

Expected:

```text
Passed!  - Failed: 0
```

---

### Task 5: Windows AD Posture Provider

**Files:**
- Create: `src/Drm.Agent.Core/WindowsDomainPosture.cs`
- Modify: `src/Drm.Agent.Service.Windows/Worker.cs`
- Modify: `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`

- [ ] **Step 1: Add provider**

Create `WindowsDomainPosture.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Drm.Agent.Core;

public static class WindowsDomainPosture
{
    private enum NetJoinStatus
    {
        NetSetupUnknownStatus = 0,
        NetSetupUnjoined = 1,
        NetSetupWorkgroupName = 2,
        NetSetupDomainName = 3
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NetGetJoinInformation(
        string? server,
        out IntPtr nameBuffer,
        out NetJoinStatus bufferType);

    [DllImport("Netapi32.dll", SetLastError = true)]
    private static extern int NetApiBufferFree(IntPtr buffer);

    public static AgentDevicePosture Capture()
    {
        if (!OperatingSystem.IsWindows())
        {
            return AgentDevicePosture.Unknown;
        }

        var joined = TryReadJoinStatus(out var domainName);
        var windowsUser = ReadWindowsUser();
        return new AgentDevicePosture(joined, domainName, windowsUser);
    }

    private static bool TryReadJoinStatus(out string domainName)
    {
        domainName = string.Empty;
        var result = NetGetJoinInformation(null, out var buffer, out var status);
        try
        {
            if (result != 0 || buffer == IntPtr.Zero)
            {
                return false;
            }

            domainName = Marshal.PtrToStringUni(buffer)?.Trim() ?? string.Empty;
            return status == NetJoinStatus.NetSetupDomainName && domainName.Length > 0;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NetApiBufferFree(buffer);
            }
        }
    }

    private static string ReadWindowsUser()
    {
        var domain = Environment.UserDomainName;
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(domain)
            ? user.Trim()
            : $"{domain.Trim()}\\{user.Trim()}";
    }
}
```

- [ ] **Step 2: Send posture from Windows service heartbeat**

In `Worker.cs`, change the heartbeat call to:

```csharp
await heartbeatWorkflow.ReportOnlineAsync(
    currentOptions.ToIdentity(),
    Environment.MachineName,
    Environment.OSVersion.VersionString,
    currentOptions.AgentVersion,
    WindowsDomainPosture.Capture(),
    stoppingToken);
```

- [ ] **Step 3: Capture posture in tray when protecting**

In `MainWindow.xaml.cs`, before creating `ProtectFileWorkflow`, add:

```csharp
var posture = WindowsDomainPosture.Capture();
```

When the tray needs to register/heartbeat during protect, use the same `DrmServerClient` behavior. The protect workflow does not currently register device directly; rely on the service heartbeat for enforcement. If the service is not installed, the server will deny with `device_trust_expired` or `device_not_domain_joined`, which is the correct fail-closed behavior for internal CAD mode.

- [ ] **Step 4: Build Agent Core and Windows service**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet build src/Drm.Agent.Core/Drm.Agent.Core.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
```

Expected:

```text
Build succeeded.
```

---

### Task 6: Internal CAD Protect UX in Windows Tray

**Files:**
- Modify: `src/Drm.Agent.Tray.Windows/MainWindow.xaml`
- Modify: `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`
- Modify: `src/Drm.Agent.Shell.Windows/install.ps1`
- Modify: `src/Drm.Agent.Shell.Windows/status.ps1`
- Test: `tests/Drm.Server.Tests/ShellIntegrationScriptTests.cs`

- [ ] **Step 1: Add CAD extension helper and unit-testable behavior**

In `MainWindow.xaml.cs`, add:

```csharp
private static readonly HashSet<string> CadExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    ".dwg",
    ".dxf",
    ".dwt",
    ".dws",
    ".step",
    ".stp",
    ".iges",
    ".igs",
    ".sldprt",
    ".sldasm",
    ".slddrw",
    ".x_t",
    ".x_b",
    ".prt",
    ".asm",
    ".catpart",
    ".catproduct",
    ".jt",
    ".ifc",
    ".sat",
    ".stl"
};

private static bool IsSupportedCadFile(string path)
    => CadExtensions.Contains(Path.GetExtension(path));
```

- [ ] **Step 2: Reword XAML primary panel**

In `MainWindow.xaml`, replace primary panel visible copy:

```xml
<TextBlock Text="Protect CAD file (internal)" FontSize="14" FontWeight="SemiBold"
           Foreground="#1f2937" />
<TextBlock Text="Encrypt CAD files for AD-joined company devices. No password, guest link, or external recipient."
           Foreground="#6B7280" Margin="0,2,0,8" TextWrapping="Wrap" FontSize="11" />
```

Replace the recipient row with an internal-only status row:

```xml
<StackPanel Orientation="Horizontal" Margin="0,8,0,0">
    <Button x:Name="QuickBrowseButton" Content="Browse CAD" Margin="0,0,0,0"
            Padding="10,2" Click="QuickBrowseButton_Click" />
    <Button x:Name="QuickSendButton" Content="Protect CAD file"
            Margin="8,0,0,0" Padding="14,4" Background="#A45B13" Foreground="White"
            FontWeight="SemiBold" BorderThickness="0"
            Click="QuickSendButton_Click" />
    <TextBlock Text="Internal AD policy is checked when the file is opened."
               Foreground="#6B7280" FontSize="11" VerticalAlignment="Center"
               Margin="10,0,0,0" />
</StackPanel>
```

Leave `QuickRecipientBox` in XAML only if removing it causes broad code churn. If left in place, set `Visibility="Collapsed"` and remove visible "Send to" copy.

- [ ] **Step 3: Restrict browse dialog to CAD**

In `QuickBrowseButton_Click`, replace filter:

```csharp
Filter = "CAD files (*.dwg;*.dxf;*.step;*.stp;*.iges;*.igs;*.sldprt;*.sldasm;*.stl)|*.dwg;*.dxf;*.step;*.stp;*.iges;*.igs;*.sldprt;*.sldasm;*.stl|All files (*.*)|*.*",
Title = "Select CAD file to protect"
```

- [ ] **Step 4: Change primary button handler to protect only**

In `QuickSendButton_Click`, remove recipient validation, external share-link creation, clipboard share URL, and mailto calls. Use:

```csharp
if (string.IsNullOrEmpty(quickPickedFile))
{
    QuickResultText.Text = "Drop a CAD file or click Browse CAD first.";
    return;
}

if (!IsSupportedCadFile(quickPickedFile))
{
    QuickResultText.Text = "This internal flow only protects CAD files.";
    return;
}

QuickSendButton.IsEnabled = false;
QuickResultText.Text = "Encrypting CAD file...";
try
{
    var serverUrl = ParseServerUrl();
    var tenantId = ParseRequiredGuid(TenantIdBox.Text, "Tenant ID");
    var userId = ParseRequiredGuid(UserIdBox.Text, "User ID");
    var clientApiKey = ClientApiKeyBox.Password.Trim();

    using var httpClient = new HttpClient { BaseAddress = serverUrl };
    var serverClient = new DrmServerClient(httpClient, clientApiKey);
    var inventory = new JsonProtectedFileInventory(ResolveDataPath("protected-inventory.json"));
    var keyStore = new JsonFileKeyStore(ResolveDataPath("file-keys.json"));
    var workflow = new ProtectFileWorkflow(serverClient, inventory, keyStore);

    var result = await workflow.ProtectAsync(
        new TenantId(tenantId),
        new UserId(userId),
        quickPickedFile,
        EnvelopeCrypto.GenerateKey(),
        new ProtectFilePolicyOptions(Permission.View, PolicyTemplateId: null, Recipients: []),
        deleteOriginalAfterProtection: false,
        CancellationToken.None);

    QuickResultText.Text =
        $"Protected internally: {Path.GetFileName(result.DestinationPath)}. " +
        "Opening requires an AD-joined trusted device.";
}
catch (Exception ex)
{
    QuickResultText.Text = $"Protect failed: {ex.Message}";
}
finally
{
    QuickSendButton.IsEnabled = true;
}
```

- [ ] **Step 5: Keep advanced external flows out of the primary demo**

Do not delete `/share/`, `/me/`, `QuickShareEndpoints`, or admin share-link APIs in this task. They remain compiled for older demos and tests. This task changes the Windows tray primary flow and docs only.

- [ ] **Step 6: Update shell labels**

In `install.ps1`, change label:

```powershell
@{ Verb = "Drm.QuickSend"; Label = "Protect CAD file (internal)"; Exe = $tray; Argument = "--quick-protect" }
```

In `ShellIntegrationScriptTests.cs`, update the asserted label if the test checks user-visible text. Keep the registry verb `Drm.QuickSend` unchanged to minimize installer churn.

- [ ] **Step 7: Build tray and run shell tests**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "FullyQualifiedName~ShellIntegrationScriptTests"
```

Expected:

```text
Build succeeded.
Passed!  - Failed: 0
```

---

### Task 7: Admin Console Device Trust Controls

**Files:**
- Modify: `src/Drm.Server/wwwroot/admin/index.html`
- Modify: `src/Drm.Server/wwwroot/admin/app.js`
- Test: `tests/Drm.Server.Tests/ManagementConsoleTests.cs`

- [ ] **Step 1: Add UI controls**

In the `deviceTrust` section of `index.html`, add controls next to the existing check-in fields:

```html
<label>
  <input id="deviceTrustRequireDomainJoined" type="checkbox">
  Require on-prem AD domain join
</label>
<label>
  Allowed AD domains
  <input id="deviceTrustAllowedDomains" autocomplete="off" placeholder="CORP, ENGINEERING">
</label>
```

- [ ] **Step 2: Wire refresh/save in JS**

In `refreshDeviceTrust()`:

```javascript
document.getElementById("deviceTrustRequireDomainJoined").checked = config.requireDomainJoined === true;
document.getElementById("deviceTrustAllowedDomains").value = (config.allowedAdDomains || []).join(", ");
```

In save handler body:

```javascript
requireDomainJoined: document.getElementById("deviceTrustRequireDomainJoined").checked,
allowedAdDomains: document.getElementById("deviceTrustAllowedDomains").value
  .split(",")
  .map((domain) => domain.trim())
  .filter(Boolean)
```

- [ ] **Step 3: Add static asset test**

In `ManagementConsoleTests.cs`, add:

```csharp
[Fact]
public async Task AdminConsole_includes_ad_device_trust_controls()
{
    using var client = factory.CreateClient();
    var html = await client.GetStringAsync("/admin/");

    html.Should().Contain("deviceTrustRequireDomainJoined");
    html.Should().Contain("deviceTrustAllowedDomains");
    html.Should().Contain("Require on-prem AD domain join");
}
```

- [ ] **Step 4: Run console tests**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "FullyQualifiedName~ManagementConsoleTests"
```

Expected:

```text
Passed!  - Failed: 0
```

---

### Task 8: Demo and Product Docs

**Files:**
- Modify: `README.md`
- Modify: `docs/demo/README.md`
- Modify: `docs/demo/02-the-3-links.md`
- Modify: `docs/demo/03-demo-script.md`
- Modify: `docs/demo/05-preflight-checklist.md`
- Modify: `docs/demo/08-engineer-windows-msi-setup.md`
- Modify: `docs/demo/10-ciso-answer-script.md`

- [ ] **Step 1: Update README positioning**

Add near the top after product bullets:

```markdown
## Current Customer Demo Scope

The active customer flow is internal CAD protection for on-prem Active Directory environments:

- protect CAD files into `.drmx`
- open only from trusted Windows devices that are registered, recently checked in, and joined to an allowed AD domain
- no user-entered DRM password or container passphrase in the primary flow
- no external guest share link in the primary flow

External sharing APIs remain in the codebase for broader product scenarios, but they are not part of this customer's CAD-only internal workflow.
```

- [ ] **Step 2: Rewrite demo story**

In `docs/demo/README.md`, replace the old 3-surface story with:

```markdown
## Demo story

> "ABC Engineering wants to protect CAD drawings used only inside the company.
> The user does not type a DRM password. zcrDRM checks the Windows device and
> AD login posture: the workstation must be registered, recently checked in,
> and joined to the approved on-prem AD domain. If the laptop leaves control
> or is disabled, protected CAD files stop opening."

Demo flow:

1. Admin enables device trust and allowed AD domains.
2. Engineer right-clicks a CAD file and chooses "Protect CAD file (internal)".
3. The protected `.drmx` opens only on trusted AD-joined devices.
4. Admin disables the device; the same file is denied.
```

- [ ] **Step 3: Remove external share as primary demo step**

In `docs/demo/02-the-3-links.md` and `docs/demo/03-demo-script.md`, keep `/admin/` and Windows Agent as primary surfaces. Move `/share/` to a "legacy/fallback external sharing" note, not the main customer story.

Use this exact wording where helpful:

```markdown
For this customer, do not demo guest email verification or external share links unless asked. Their requirement is internal CAD protection with AD-joined device checks.
```

- [ ] **Step 4: Update preflight**

In `docs/demo/05-preflight-checklist.md`, add:

```markdown
- [ ] Demo laptop is joined to the customer's on-prem AD domain or a test AD domain.
- [ ] Windows user is logged in as a domain account, visible as `DOMAIN\username`.
- [ ] zcrDRM device trust is enabled with the AD domain listed in Allowed AD domains.
- [ ] CAD sample file is available: `.dwg`, `.dxf`, `.step`, `.stp`, `.sldprt`, or `.stl`.
- [ ] External share link/browser guest verification is not part of the main demo path.
```

- [ ] **Step 5: Run markdown grep sanity check**

Run:

```bash
rg -n "Quick Send|share URL|guest email|verification link|passphrase" docs/demo README.md
```

Expected:

```text
Only fallback/external-sharing notes mention these terms; the main demo flow does not.
```

---

### Task 9: End-to-End Verification

**Files:**
- No code changes.

- [ ] **Step 1: Run focused test suite**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "FullyQualifiedName~V19FeatureTests|FullyQualifiedName~ManagementConsoleTests|FullyQualifiedName~ShellIntegrationScriptTests"
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "FullyQualifiedName~AgentHeartbeatWorkflowTests"
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 2: Build Windows projects from macOS with Windows targeting**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 3: Run broader safety tests if time allows**

Run:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
PATH=/Users/pop7/.dotnet:$PATH dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
```

Expected:

```text
Passed!  - Failed: 0
```

- [ ] **Step 4: Manual Windows smoke test**

On a Windows machine joined to the test AD domain:

```powershell
whoami
nltest /dsgetdc:<AD_DOMAIN_NAME>
```

Expected:

```text
whoami returns DOMAIN\username
nltest returns a domain controller for the same domain
```

Then:

1. Install/register zcrDRM Agent.
2. Confirm service heartbeat succeeds.
3. Enable Device trust in admin console.
4. Check "Require on-prem AD domain join".
5. Add the AD domain to Allowed AD domains.
6. Right-click a `.dwg` or `.step` file and choose "Protect CAD file (internal)".
7. Open the resulting `.drmx` on the trusted AD-joined device.
8. Disable the device in admin console.
9. Reopen the same `.drmx`.

Expected:

```text
Before disable: open succeeds.
After disable: open is denied with device_disabled.
If allowed domain is changed to a non-matching value: open is denied with ad_domain_not_allowed.
If the device reports not domain joined: open is denied with device_not_domain_joined.
```

---

## Commit Strategy

Commit after each green task:

```bash
git add tests/Drm.Server.Tests/V19FeatureTests.cs src/Drm.Server/Entities.cs src/Drm.Server/AppDbContext.cs src/Drm.Server/Program.cs src/Drm.Server/Endpoints/AgentEndpoints.cs src/Drm.Server/Endpoints/AdminDeviceTrustEndpoints.cs src/Drm.Server/PolicyDecisionService.cs
git commit -m "feat: enforce AD domain device trust"
```

```bash
git add src/Drm.Agent.Core/AgentIdentity.cs src/Drm.Agent.Core/DrmServerClient.cs src/Drm.Agent.Core/AgentHeartbeatWorkflow.cs src/Drm.Agent.Core/WindowsDomainPosture.cs src/Drm.Agent.Service.Windows/Worker.cs tests/Drm.Agent.Core.Tests/AgentHeartbeatWorkflowTests.cs
git commit -m "feat: report Windows AD posture from agent"
```

```bash
git add src/Drm.Agent.Tray.Windows/MainWindow.xaml src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs src/Drm.Agent.Shell.Windows/install.ps1 src/Drm.Agent.Shell.Windows/status.ps1 tests/Drm.Server.Tests/ShellIntegrationScriptTests.cs
git commit -m "feat: add internal CAD protect flow"
```

```bash
git add src/Drm.Server/wwwroot/admin/index.html src/Drm.Server/wwwroot/admin/app.js tests/Drm.Server.Tests/ManagementConsoleTests.cs docs/demo README.md
git commit -m "docs: align demo with internal CAD AD trust"
```

---

## Self-Review

- Spec coverage: covers internal CAD-only protection, no password/passphrase in primary flow, on-prem AD domain join detection, AD user posture capture, server-side device trust enforcement, admin configuration, docs, and verification.
- Scope control: external share APIs remain compiled but move out of the primary demo. Removing those APIs is not part of this plan because it would break existing tests and broader product capabilities.
- Type consistency: `AgentDevicePosture`, `DomainJoined`, `DomainName`, `WindowsUser`, `RequireDomainJoined`, and `AllowedAdDomainsCsv` are used consistently across tasks.
- Risk: server trust is based on the installed agent's reported Windows posture. Production hardening can add signed device attestation or Kerberos-backed proof, but that requires domain-controller/service-account design beyond this customer demo requirement.
