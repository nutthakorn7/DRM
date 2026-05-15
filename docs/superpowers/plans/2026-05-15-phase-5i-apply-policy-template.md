# Phase 5I Apply Policy Template Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let administrators apply an existing policy template to a protected file so template permissions and watermark text become active file policy.

**Architecture:** Extend the existing admin files endpoint with `POST /api/admin/files/{fileId}/apply-policy-template`. The endpoint is tenant-scoped, loads the protected file and policy template in the same tenant, updates file permissions and watermark template, synchronizes the owner grant to the template permissions, and writes a permission audit event.

**Tech Stack:** .NET 10 minimal APIs, EF Core, vanilla JS admin console, xUnit, FluentAssertions.

---

## File Structure

- Modify `tests/Drm.Server.Tests/AdminFilesApiTests.cs`: add apply-template API coverage.
- Modify `tests/Drm.Server.Tests/ManagementConsoleTests.cs`: assert the console exposes apply-template UI and JS wiring.
- Modify `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs`: map and implement `POST /api/admin/files/{fileId}/apply-policy-template`.
- Modify `src/Drm.Server/wwwroot/admin/index.html`: add template ID and apply action in the Protected files section.
- Modify `src/Drm.Server/wwwroot/admin/app.js`: submit apply-template requests and refresh files.
- Modify `src/Drm.Server/wwwroot/admin/app.css`: keep the file action row responsive.
- Modify `README.md`: document Phase 5I.

## Tasks

### Task 1: Apply Template API

- [x] **Step 1: Write failing API tests**

Add tests to `tests/Drm.Server.Tests/AdminFilesApiTests.cs`:

```csharp
[Fact]
public async Task Admin_can_apply_policy_template_to_file()
{
    using var client = factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    var ownerUserId = Guid.NewGuid();
    var templateId = Guid.NewGuid();
    var adminUserId = Guid.NewGuid();

    using var register = await RegisterFileAsync(client, tenantId, fileId, ownerUserId, permissions: "View");
    using var createTemplate = await CreatePolicyTemplateAsync(
        client,
        tenantId,
        templateId,
        "Restricted",
        "View, Print",
        "restricted:{userId}:{fileId}");
    register.StatusCode.Should().Be(HttpStatusCode.Created);
    createTemplate.StatusCode.Should().Be(HttpStatusCode.Created);

    using var apply = await ApplyPolicyTemplateAsync(client, tenantId, fileId, templateId, adminUserId);

    apply.StatusCode.Should().Be(HttpStatusCode.OK);
    var applied = await apply.Content.ReadFromJsonAsync<FileResponse>();
    applied.Should().BeEquivalentTo(new
    {
        TenantId = tenantId,
        FileId = fileId,
        OwnerUserId = ownerUserId,
        Permissions = "View, Print",
        WatermarkTemplate = "restricted:{userId}:{fileId}",
        Revoked = false
    });
}
```

Also assert policy decisions use the applied permissions/watermark, the owner grant is synchronized, missing file/template returns `404`, cross-tenant template returns `404`, and audit contains `permission_changed/policy_template_applied`.

- [x] **Step 2: Run failing API test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Admin_can_apply_policy_template_to_file|Admin_apply_policy_template_returns_not_found_for_missing_file_or_template"
```

Expected: FAIL because `/api/admin/files/{fileId}/apply-policy-template` does not exist.

- [x] **Step 3: Implement API**

In `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs`:

```csharp
group.MapPost("/{fileId:guid}/apply-policy-template", ApplyPolicyTemplateAsync);
```

Add:

```csharp
private static async Task<Results<Ok<FileResponse>, NotFound>> ApplyPolicyTemplateAsync(
    Guid fileId,
    ApplyPolicyTemplateRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken)
{
    var file = await dbContext.ProtectedFiles.SingleOrDefaultAsync(
        candidate => candidate.TenantId == request.TenantId && candidate.Id == fileId,
        cancellationToken);
    var template = await dbContext.PolicyTemplates.AsNoTracking().SingleOrDefaultAsync(
        candidate => candidate.TenantId == request.TenantId && candidate.TemplateId == request.TemplateId,
        cancellationToken);
    if (file is null || template is null)
    {
        return TypedResults.NotFound();
    }

    if (!PermissionParser.TryParse(template.Permissions, out var permissions))
    {
        return TypedResults.NotFound();
    }

    file.Permissions = permissions;
    file.WatermarkTemplate = template.WatermarkTemplate;
    await UpsertOwnerGrantFromTemplateAsync(dbContext, file, permissions, cancellationToken);
    dbContext.AuditEvents.Add(AdminAudit.PermissionEvent(request.TenantId, fileId, request.AdminUserId, "policy_template_applied"));
    await dbContext.SaveChangesAsync(cancellationToken);

    return TypedResults.Ok(FileResponse.From(file));
}
```

- [x] **Step 4: Run passing API test**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Admin_can_apply_policy_template_to_file|Admin_apply_policy_template_returns_not_found_for_missing_file_or_template"
```

Expected: PASS.

### Task 2: Console UI

- [x] **Step 1: Write failing console tests**

Update `tests/Drm.Server.Tests/ManagementConsoleTests.cs` to assert HTML contains `Apply policy template` and `Policy template ID`, and JS contains `/apply-policy-template` and `applyPolicyTemplateForm`.

- [x] **Step 2: Run failing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: FAIL until console assets are updated.

- [x] **Step 3: Implement console controls**

In `src/Drm.Server/wwwroot/admin/index.html`, add a form in the Protected files panel:

```html
<form class="apply-template-row" id="applyPolicyTemplateForm">
  <input id="applyTemplateFileId" autocomplete="off" placeholder="File ID">
  <input id="applyPolicyTemplateId" autocomplete="off" placeholder="Policy template ID">
  <button type="submit">Apply policy template</button>
</form>
```

In `src/Drm.Server/wwwroot/admin/app.js`, submit to:

```js
await apiFetch(`/api/admin/files/${encodeURIComponent(fileId)}/apply-policy-template`, {
  method: "POST",
  body: JSON.stringify({ tenantId: requireTenantId(), templateId, adminUserId: requireAdminUserId() })
});
```

- [x] **Step 4: Run passing console tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementConsoleTests
```

Expected: PASS.

### Task 3: Verification and Commit

- [x] **Step 1: Update README**

Add Phase 5I notes for applying policy templates to protected files.

- [x] **Step 2: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 3: Live smoke**

Run a temp server, register a file, create a policy template, apply it through the admin API, list the file, verify the applied permissions and watermark, and use Playwright to check `/admin/` has no console errors.

- [x] **Step 4: Commit**

Run:

```bash
git add README.md src/Drm.Server tests/Drm.Server.Tests docs/superpowers/plans/2026-05-15-phase-5i-apply-policy-template.md
git commit -m "feat: apply policy templates to files"
```

## Self-Review

- Spec coverage: Connects policy templates to active file policy, matching the enterprise DRM design requirement to assign policy templates to protected files.
- Security note: Tenant-scoped lookup prevents applying templates across tenants; admin auth remains enforced by the existing middleware when configured.
- Placeholder scan: No TBD/TODO placeholders.
