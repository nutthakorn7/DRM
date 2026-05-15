# Phase 5J Desktop Template Recipients Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the desktop protect flow register files with a policy template and explicit user/group recipients.

**Architecture:** Extend the existing client file registration endpoint (`POST /api/files`) with optional `policyTemplateId` and `recipients`. When a template is supplied, the server copies its permissions and watermark into the protected file and grants the same effective permissions to the owner plus requested recipients. Agent core gets a typed registration/options model, and the tray UI collects template and recipient IDs.

**Tech Stack:** .NET 10 minimal APIs, EF Core, WPF tray app, xUnit, FluentAssertions.

---

## File Structure

- Modify `tests/Drm.Server.Tests/PolicyApiTests.cs`: server endpoint tests for template-backed registration and recipient grants.
- Modify `tests/Drm.Agent.Core.Tests/AgentClientTests.cs`: HTTP client serialization test for template/recipients.
- Modify `tests/Drm.Agent.Core.Tests/ProtectPdfFileWorkflowTests.cs`: workflow test proving options reach the server client.
- Modify `src/Drm.Server/Endpoints/FilesEndpoints.cs`: accept optional policy template and recipient payloads.
- Modify `src/Drm.Agent.Core/DrmServerClient.cs`: add typed registration/options records and serialize them.
- Modify `src/Drm.Agent.Core/ProtectPdfFileWorkflow.cs`: accept policy options while keeping the old overload.
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml`: add fields for template ID and recipients.
- Modify `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs`: parse fields and pass policy options.
- Modify `README.md`: document Phase 5J.

## Tasks

### Task 1: Server Registration Template and Recipients

- [x] **Step 1: Write failing server tests**

Add tests to `tests/Drm.Server.Tests/PolicyApiTests.cs`:

```csharp
[Fact]
public async Task Registering_file_with_policy_template_and_recipients_applies_template_policy()
{
    using var client = factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    var ownerUserId = Guid.NewGuid();
    var directRecipientUserId = Guid.NewGuid();
    var groupId = Guid.NewGuid();
    var groupMemberUserId = Guid.NewGuid();
    var templateId = Guid.NewGuid();

    using var createGroup = await client.PostAsJsonAsync("/api/admin/groups", new { tenantId, groupId, name = "Legal" });
    using var addMember = await client.PostAsJsonAsync($"/api/admin/groups/{groupId}/members", new { tenantId, userId = groupMemberUserId });
    using var createTemplate = await client.PostAsJsonAsync("/api/admin/policy-templates", new
    {
        tenantId,
        templateId,
        name = "Restricted",
        permissions = "View, Print",
        watermarkTemplate = "restricted:{userId}:{fileId}",
        offlineLeaseMinutes = 15,
        allowPrint = true
    });
    createGroup.StatusCode.Should().Be(HttpStatusCode.Created);
    addMember.StatusCode.Should().Be(HttpStatusCode.Created);
    createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

    using var register = await client.PostAsJsonAsync("/api/files", new
    {
        tenantId,
        fileId,
        ownerUserId,
        contentType = "application/pdf",
        expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        permissions = "View",
        watermarkTemplate = "old:{userId}",
        policyTemplateId = templateId,
        recipients = new[]
        {
            new { subjectType = "User", subjectId = directRecipientUserId },
            new { subjectType = "Group", subjectId = groupId }
        }
    });

    register.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

Complete the test by asserting the register response uses template permissions/watermark and policy decisions allow `Print` for owner, direct recipient, and group member.

- [x] **Step 2: Run failing server tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Registering_file_with_policy_template_and_recipients_applies_template_policy|Registering_file_with_missing_template_or_group_recipient_returns_not_found"
```

Expected: FAIL because `/api/files` ignores template and recipients.

- [x] **Step 3: Implement server registration support**

Update `src/Drm.Server/Endpoints/FilesEndpoints.cs` to:

- Load `PolicyTemplateEntity` when `policyTemplateId` is present.
- Use template permissions and watermark when present.
- Add owner grant plus recipient grants with the effective permissions.
- Validate recipient subject type, duplicate recipients, empty subject IDs, and missing groups.
- Return `404` for missing policy template or missing group.

- [x] **Step 4: Run passing server tests**

Run the same filtered server test command. Expected: PASS.

### Task 2: Agent Core Registration Options

- [x] **Step 1: Write failing agent core tests**

Add tests that assert:

- `DrmServerClient.RegisterFileAsync(ProtectedFileRegistration, ...)` posts `policyTemplateId` and `recipients` to `/api/files`.
- `ProtectPdfFileWorkflow.ProtectAsync(..., ProtectPdfPolicyOptions, ...)` passes the template ID and recipients to the server client.

- [x] **Step 2: Run failing agent core tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj --filter "DrmServerClient_posts_template_and_recipients_when_registering_file|ProtectPdfFileWorkflow_passes_policy_options_to_server_registration"
```

Expected: FAIL because the typed registration/options API does not exist.

- [x] **Step 3: Implement agent core registration options**

Add public records in `src/Drm.Agent.Core/DrmServerClient.cs`:

```csharp
public sealed record ProtectedFileRegistration(
    Guid TenantId,
    Guid FileId,
    Guid OwnerUserId,
    string ContentType,
    DateTimeOffset ExpiresAtUtc,
    Permission Permissions,
    Guid? PolicyTemplateId,
    IReadOnlyList<ProtectionRecipient> Recipients);

public sealed record ProtectionRecipient(string SubjectType, Guid SubjectId);
```

Add overloads/default interface method and use them from `ProtectPdfFileWorkflow`.

- [x] **Step 4: Run passing agent core tests**

Run the same filtered agent core test command. Expected: PASS.

### Task 3: Tray UI and Verification

- [x] **Step 1: Implement tray fields**

Add `Policy template ID`, `Recipient user IDs`, and `Recipient group IDs` fields to `src/Drm.Agent.Tray.Windows/MainWindow.xaml`.

- [x] **Step 2: Wire tray parsing**

Update `src/Drm.Agent.Tray.Windows/MainWindow.xaml.cs` to parse optional template ID and comma/semicolon-separated recipient IDs, then call the new workflow overload.

- [x] **Step 3: Update README**

Add Phase 5J notes for template/recipient desktop protection.

- [x] **Step 4: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 5: Live smoke**

Run a temp server, create a group/member and policy template, register a file through `/api/files` with `policyTemplateId` and recipients, then verify owner/direct/group policy decisions use template permissions and watermark.

- [x] **Step 6: Commit**

Run:

```bash
git add README.md src/Drm.Server src/Drm.Agent.Core src/Drm.Agent.Tray.Windows tests docs/superpowers/plans/2026-05-15-phase-5j-desktop-template-recipients.md
git commit -m "feat: protect files with templates and recipients"
```

## Self-Review

- Spec coverage: Connects the desktop protect path to policy templates and recipients without requiring desktop clients to call admin-only endpoints.
- Security note: This is still MVP client-authorized registration; production must replace the shared client key with user/device identity and server-side entitlement checks.
- Placeholder scan: No TBD/TODO placeholders.
