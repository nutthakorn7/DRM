# Enterprise DRM Phase 2A Admin and Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the first enterprise-admin slice on top of the Foundation MVP: multi-recipient grants, admin file search, policy templates, audit filtering/CSV export, permission-change/system logs, and SIEM webhook delivery.

**Architecture:** Keep `Drm.Domain` pure, keep `Drm.Agent.Core` server-independent, and evolve `Drm.Server` with explicit admin API endpoints plus persistence entities for users, groups, policy templates, file grants, and SIEM webhooks. This phase deliberately does not implement AD, Entra ID, SAML, SCIM, or production email delivery; it creates the admin/audit data model those integrations will plug into.

**Tech Stack:** .NET 10 LTS, ASP.NET Core Minimal APIs, EF Core, SQLite tests, xUnit, FluentAssertions, `WebApplicationFactory<Program>`.

---

## Scope Boundary

This plan implements **Phase 2A: Admin and Audit Foundation**. It is the next safe slice after the Foundation MVP already merged to `master`.

Included:

- Tenant admin bootstrap data.
- Local users and groups.
- Group membership.
- Policy templates.
- File grants for multiple users/groups.
- Policy decisions based on owner, direct user grants, and group grants.
- File search.
- Bulk grant updates.
- Permission-change and system audit events.
- Audit filtering and CSV export.
- SIEM webhook registration and event delivery through a testable abstraction.
- README updates.

Excluded and planned separately:

- AD and Microsoft Entra ID sync.
- SAML/OIDC login.
- SCIM provisioning.
- Email notifications through a real provider.
- Access request approval UI.
- Browser admin console frontend.
- Production database migrations.
- Production SIEM authentication/signing.

## File Structure

Create or modify:

```text
src/Drm.Domain/Subject.cs
src/Drm.Server/Entities.cs
src/Drm.Server/AppDbContext.cs
src/Drm.Server/AdminAudit.cs
src/Drm.Server/PermissionParser.cs
src/Drm.Server/Endpoints/AdminUsersEndpoints.cs
src/Drm.Server/Endpoints/AdminGroupsEndpoints.cs
src/Drm.Server/Endpoints/AdminPolicyTemplatesEndpoints.cs
src/Drm.Server/Endpoints/AdminFilesEndpoints.cs
src/Drm.Server/Endpoints/AdminAuditEndpoints.cs
src/Drm.Server/Endpoints/AdminSiemEndpoints.cs
src/Drm.Server/Endpoints/FilesEndpoints.cs
src/Drm.Server/Endpoints/PolicyEndpoints.cs
src/Drm.Server/Program.cs
tests/Drm.Server.Tests/AdminUsersApiTests.cs
tests/Drm.Server.Tests/AdminGroupsApiTests.cs
tests/Drm.Server.Tests/AdminPolicyTemplatesApiTests.cs
tests/Drm.Server.Tests/AdminFilesApiTests.cs
tests/Drm.Server.Tests/AdminAuditApiTests.cs
tests/Drm.Server.Tests/AdminSiemWebhookTests.cs
tests/Drm.Server.Tests/PolicyApiTests.cs
tests/Drm.Integration.Tests/ServerIntegratedWorkflowTests.cs
README.md
```

Boundaries:

- `Drm.Domain/Subject.cs` defines grant subject types only. No EF or HTTP references.
- `Drm.Server/Entities.cs` owns persistence entities.
- Admin endpoints live in separate files by capability.
- Policy decisions continue to route through `PolicyEvaluator`; server code builds the applicable grants.
- SIEM delivery is behind an interface so tests can use an in-memory sink.

## Task 1: Add Grant Subjects and Server Entities

**Files:**
- Create: `src/Drm.Domain/Subject.cs`
- Modify: `src/Drm.Server/Entities.cs`
- Modify: `src/Drm.Server/AppDbContext.cs`
- Create: `tests/Drm.Server.Tests/AdminUsersApiTests.cs`

- [ ] **Step 1: Write failing entity-backed admin user test**

Create `tests/Drm.Server.Tests/AdminUsersApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminUsersApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-users-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminUsersApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_create_and_list_users_for_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var create = await client.PostAsJsonAsync("/api/admin/users", new
        {
            tenantId,
            userId,
            email = "owner@example.com",
            displayName = "Owner User"
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var users = await client.GetFromJsonAsync<List<UserResponse>>($"/api/admin/users?tenantId={tenantId}");

        users.Should().NotBeNull();
        users.Should().ContainSingle(user =>
            user.UserId == userId &&
            user.Email == "owner@example.com" &&
            user.DisplayName == "Owner User");
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record UserResponse(Guid UserId, Guid TenantId, string Email, string DisplayName);
}
```

- [ ] **Step 2: Run test to verify failure**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter Admin_can_create_and_list_users_for_tenant
```

Expected: fails because admin user endpoints/entities do not exist.

- [ ] **Step 3: Add domain subject type**

Create `src/Drm.Domain/Subject.cs`:

```csharp
namespace Drm.Domain;

public enum GrantSubjectType
{
    User = 1,
    Group = 2
}
```

- [ ] **Step 4: Extend server entities**

Append these entities to `src/Drm.Server/Entities.cs`:

```csharp
public sealed class TenantUserEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class TenantGroupEntity
{
    public Guid TenantId { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class GroupMemberEntity
{
    public Guid TenantId { get; set; }
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class PolicyTemplateEntity
{
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public string Name { get; set; } = "";
    public string Permissions { get; set; } = "";
    public string WatermarkTemplate { get; set; } = "";
    public int OfflineLeaseMinutes { get; set; }
    public bool AllowPrint { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class FileGrantEntity
{
    public Guid TenantId { get; set; }
    public Guid FileId { get; set; }
    public string SubjectType { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string Permissions { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class SiemWebhookEntity
{
    public Guid TenantId { get; set; }
    public Guid WebhookId { get; set; }
    public string Url { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

- [ ] **Step 5: Extend DbContext**

Modify `src/Drm.Server/AppDbContext.cs` to add sets and composite keys:

```csharp
public DbSet<TenantUserEntity> TenantUsers => Set<TenantUserEntity>();
public DbSet<TenantGroupEntity> TenantGroups => Set<TenantGroupEntity>();
public DbSet<GroupMemberEntity> GroupMembers => Set<GroupMemberEntity>();
public DbSet<PolicyTemplateEntity> PolicyTemplates => Set<PolicyTemplateEntity>();
public DbSet<FileGrantEntity> FileGrants => Set<FileGrantEntity>();
public DbSet<SiemWebhookEntity> SiemWebhooks => Set<SiemWebhookEntity>();
```

Inside `OnModelCreating`, add:

```csharp
modelBuilder.Entity<TenantUserEntity>().HasKey(user => new { user.TenantId, user.UserId });
modelBuilder.Entity<TenantUserEntity>().HasIndex(user => new { user.TenantId, user.Email }).IsUnique();
modelBuilder.Entity<TenantGroupEntity>().HasKey(group => new { group.TenantId, group.GroupId });
modelBuilder.Entity<GroupMemberEntity>().HasKey(member => new { member.TenantId, member.GroupId, member.UserId });
modelBuilder.Entity<PolicyTemplateEntity>().HasKey(template => new { template.TenantId, template.TemplateId });
modelBuilder.Entity<FileGrantEntity>().HasKey(grant => new { grant.TenantId, grant.FileId, grant.SubjectType, grant.SubjectId });
modelBuilder.Entity<SiemWebhookEntity>().HasKey(webhook => new { webhook.TenantId, webhook.WebhookId });
```

- [ ] **Step 6: Add admin users endpoint**

Create `src/Drm.Server/Endpoints/AdminUsersEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminUsersEndpoints
{
    public static IEndpointRouteBuilder MapAdminUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/users");
        group.MapPost("/", CreateUserAsync);
        group.MapGet("/", ListUsersAsync);
        return endpoints;
    }

    private static async Task<Results<Created<UserResponse>, Conflict>> CreateUserAsync(
        CreateUserRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.TenantUsers
            .AnyAsync(user => user.TenantId == request.TenantId && user.UserId == request.UserId, cancellationToken);
        if (exists)
        {
            return TypedResults.Conflict();
        }

        var entity = new TenantUserEntity
        {
            TenantId = request.TenantId,
            UserId = request.UserId,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TenantUsers.Add(entity);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, request.UserId, "user_created"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/users/{entity.UserId}", UserResponse.From(entity));
    }

    private static async Task<IReadOnlyList<UserResponse>> ListUsersAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.TenantUsers
            .AsNoTracking()
            .Where(user => user.TenantId == tenantId)
            .OrderBy(user => user.Email)
            .Select(user => UserResponse.From(user))
            .ToListAsync(cancellationToken);
    }

    private sealed record CreateUserRequest(Guid TenantId, Guid UserId, string Email, string DisplayName);

    private sealed record UserResponse(Guid UserId, Guid TenantId, string Email, string DisplayName)
    {
        public static UserResponse From(TenantUserEntity user)
            => new(user.UserId, user.TenantId, user.Email, user.DisplayName);
    }
}
```

Create `src/Drm.Server/AdminAudit.cs`:

```csharp
namespace Drm.Server;

public static class AdminAudit
{
    public static AuditEventEntity SystemEvent(Guid tenantId, Guid? userId, string reasonCode)
        => new()
        {
            TenantId = tenantId,
            UserId = userId,
            EventType = "system_changed",
            ReasonCode = reasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    public static AuditEventEntity PermissionEvent(Guid tenantId, Guid fileId, Guid? userId, string reasonCode)
        => new()
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            EventType = "permission_changed",
            ReasonCode = reasonCode,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
}
```

Modify `src/Drm.Server/Program.cs`:

```csharp
app.MapAdminUsersEndpoints();
```

- [ ] **Step 7: Verify and commit**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter Admin_can_create_and_list_users_for_tenant
/Users/pop7/.dotnet/dotnet test Drm.sln
```

Expected: tests pass.

Commit:

```bash
git add src/Drm.Domain src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: add admin user foundation"
```

## Task 2: Add Groups and Group Membership

**Files:**
- Create: `src/Drm.Server/Endpoints/AdminGroupsEndpoints.cs`
- Create: `tests/Drm.Server.Tests/AdminGroupsApiTests.cs`
- Modify: `src/Drm.Server/Program.cs`

- [ ] **Step 1: Write failing group membership test**

Create `tests/Drm.Server.Tests/AdminGroupsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminGroupsApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-groups-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminGroupsApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_create_group_and_add_member()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var createGroup = await client.PostAsJsonAsync("/api/admin/groups", new
        {
            tenantId,
            groupId,
            name = "Finance"
        });
        createGroup.StatusCode.Should().Be(HttpStatusCode.Created);

        using var addMember = await client.PostAsJsonAsync($"/api/admin/groups/{groupId}/members", new
        {
            tenantId,
            userId
        });
        addMember.StatusCode.Should().Be(HttpStatusCode.Created);

        var members = await client.GetFromJsonAsync<List<GroupMemberResponse>>($"/api/admin/groups/{groupId}/members?tenantId={tenantId}");

        members.Should().ContainSingle(member => member.UserId == userId);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record GroupMemberResponse(Guid TenantId, Guid GroupId, Guid UserId);
}
```

- [ ] **Step 2: Implement group endpoints**

Create `src/Drm.Server/Endpoints/AdminGroupsEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminGroupsEndpoints
{
    public static IEndpointRouteBuilder MapAdminGroupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/groups");
        group.MapPost("/", CreateGroupAsync);
        group.MapPost("/{groupId:guid}/members", AddMemberAsync);
        group.MapGet("/{groupId:guid}/members", ListMembersAsync);
        return endpoints;
    }

    private static async Task<Results<Created<GroupResponse>, Conflict>> CreateGroupAsync(
        CreateGroupRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.TenantGroups
            .AnyAsync(group => group.TenantId == request.TenantId && group.GroupId == request.GroupId, cancellationToken);
        if (exists)
        {
            return TypedResults.Conflict();
        }

        var entity = new TenantGroupEntity
        {
            TenantId = request.TenantId,
            GroupId = request.GroupId,
            Name = request.Name,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.TenantGroups.Add(entity);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "group_created"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/groups/{entity.GroupId}", new GroupResponse(entity.TenantId, entity.GroupId, entity.Name));
    }

    private static async Task<Results<Created<GroupMemberResponse>, Conflict>> AddMemberAsync(
        Guid groupId,
        AddGroupMemberRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.GroupMembers
            .AnyAsync(member => member.TenantId == request.TenantId && member.GroupId == groupId && member.UserId == request.UserId, cancellationToken);
        if (exists)
        {
            return TypedResults.Conflict();
        }

        var member = new GroupMemberEntity
        {
            TenantId = request.TenantId,
            GroupId = groupId,
            UserId = request.UserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.GroupMembers.Add(member);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, request.UserId, "group_member_added"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/groups/{groupId}/members/{request.UserId}", GroupMemberResponse.From(member));
    }

    private static async Task<IReadOnlyList<GroupMemberResponse>> ListMembersAsync(
        Guid groupId,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.GroupMembers
            .AsNoTracking()
            .Where(member => member.TenantId == tenantId && member.GroupId == groupId)
            .OrderBy(member => member.UserId)
            .Select(member => GroupMemberResponse.From(member))
            .ToListAsync(cancellationToken);
    }

    private sealed record CreateGroupRequest(Guid TenantId, Guid GroupId, string Name);
    private sealed record AddGroupMemberRequest(Guid TenantId, Guid UserId);
    private sealed record GroupResponse(Guid TenantId, Guid GroupId, string Name);
    private sealed record GroupMemberResponse(Guid TenantId, Guid GroupId, Guid UserId)
    {
        public static GroupMemberResponse From(GroupMemberEntity member)
            => new(member.TenantId, member.GroupId, member.UserId);
    }
}
```

Modify `Program.cs`:

```csharp
app.MapAdminGroupsEndpoints();
```

- [ ] **Step 3: Verify and commit**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter Admin_can_create_group_and_add_member
/Users/pop7/.dotnet/dotnet test Drm.sln
```

Commit:

```bash
git add src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: add admin groups"
```

## Task 3: Add Policy Templates

**Files:**
- Create: `src/Drm.Server/Endpoints/AdminPolicyTemplatesEndpoints.cs`
- Create: `tests/Drm.Server.Tests/AdminPolicyTemplatesApiTests.cs`
- Modify: `src/Drm.Server/Program.cs`

- [ ] **Step 1: Write failing policy-template test**

Create `tests/Drm.Server.Tests/AdminPolicyTemplatesApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminPolicyTemplatesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-templates-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminPolicyTemplatesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_create_and_list_policy_templates()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        using var create = await client.PostAsJsonAsync("/api/admin/policy-templates", new
        {
            tenantId,
            templateId,
            name = "Confidential View Only",
            permissions = "View",
            watermarkTemplate = "{user} {time} CONFIDENTIAL",
            offlineLeaseMinutes = 30,
            allowPrint = false
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var templates = await client.GetFromJsonAsync<List<PolicyTemplateResponse>>($"/api/admin/policy-templates?tenantId={tenantId}");

        templates.Should().ContainSingle(template =>
            template.TemplateId == templateId &&
            template.Permissions == "View" &&
            template.AllowPrint == false);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record PolicyTemplateResponse(
        Guid TenantId,
        Guid TemplateId,
        string Name,
        string Permissions,
        string WatermarkTemplate,
        int OfflineLeaseMinutes,
        bool AllowPrint);
}
```

- [ ] **Step 2: Implement policy-template endpoint**

Create `src/Drm.Server/Endpoints/AdminPolicyTemplatesEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminPolicyTemplatesEndpoints
{
    public static IEndpointRouteBuilder MapAdminPolicyTemplatesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/policy-templates");
        group.MapPost("/", CreateTemplateAsync);
        group.MapGet("/", ListTemplatesAsync);
        return endpoints;
    }

    private static async Task<Results<Created<PolicyTemplateResponse>, BadRequest<ErrorResponse>, Conflict>> CreateTemplateAsync(
        CreatePolicyTemplateRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!PermissionParser.TryParse(request.Permissions, out var permissions))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_permissions"));
        }

        var exists = await dbContext.PolicyTemplates
            .AnyAsync(template => template.TenantId == request.TenantId && template.TemplateId == request.TemplateId, cancellationToken);
        if (exists)
        {
            return TypedResults.Conflict();
        }

        var template = new PolicyTemplateEntity
        {
            TenantId = request.TenantId,
            TemplateId = request.TemplateId,
            Name = request.Name,
            Permissions = permissions.ToString(),
            WatermarkTemplate = request.WatermarkTemplate,
            OfflineLeaseMinutes = request.OfflineLeaseMinutes,
            AllowPrint = request.AllowPrint,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.PolicyTemplates.Add(template);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "policy_template_created"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/policy-templates/{template.TemplateId}", PolicyTemplateResponse.From(template));
    }

    private static async Task<IReadOnlyList<PolicyTemplateResponse>> ListTemplatesAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.PolicyTemplates
            .AsNoTracking()
            .Where(template => template.TenantId == tenantId)
            .OrderBy(template => template.Name)
            .Select(template => PolicyTemplateResponse.From(template))
            .ToListAsync(cancellationToken);
    }

    private sealed record CreatePolicyTemplateRequest(
        Guid TenantId,
        Guid TemplateId,
        string Name,
        string Permissions,
        string WatermarkTemplate,
        int OfflineLeaseMinutes,
        bool AllowPrint);

    private sealed record PolicyTemplateResponse(
        Guid TenantId,
        Guid TemplateId,
        string Name,
        string Permissions,
        string WatermarkTemplate,
        int OfflineLeaseMinutes,
        bool AllowPrint)
    {
        public static PolicyTemplateResponse From(PolicyTemplateEntity template)
            => new(
                template.TenantId,
                template.TemplateId,
                template.Name,
                template.Permissions,
                template.WatermarkTemplate,
                template.OfflineLeaseMinutes,
                template.AllowPrint);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
```

Modify `Program.cs`:

```csharp
app.MapAdminPolicyTemplatesEndpoints();
```

- [ ] **Step 3: Verify and commit**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter Admin_can_create_and_list_policy_templates
/Users/pop7/.dotnet/dotnet test Drm.sln
```

Commit:

```bash
git add src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: add policy templates"
```

## Task 4: Move File Authorization to Explicit Grants

**Files:**
- Modify: `src/Drm.Server/Endpoints/FilesEndpoints.cs`
- Modify: `src/Drm.Server/Endpoints/PolicyEndpoints.cs`
- Modify: `tests/Drm.Server.Tests/PolicyApiTests.cs`
- Create: `tests/Drm.Server.Tests/AdminFilesApiTests.cs`

- [ ] **Step 1: Add tests for owner grant and group grant decisions**

Add to `tests/Drm.Server.Tests/PolicyApiTests.cs`:

```csharp
[Fact]
public async Task Registering_file_creates_owner_file_grant()
{
    using var client = factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    var ownerUserId = Guid.NewGuid();

    using var registerResponse = await client.PostAsJsonAsync("/api/files", new RegisterFileRequest(
        tenantId,
        fileId,
        ownerUserId,
        "application/pdf",
        DateTimeOffset.UtcNow.AddHours(1),
        "View, Print",
        "user:{userId}"));

    registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    using var decideResponse = await client.PostAsJsonAsync("/api/policy/decide", new DecidePolicyRequest(
        tenantId,
        fileId,
        ownerUserId,
        Guid.NewGuid(),
        "Print",
        DateTimeOffset.UtcNow));

    decideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var decision = await decideResponse.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
    decision!.Allowed.Should().BeTrue();
    decision.AllowedPermissions.Should().Be("View, Print");
}
```

Create `tests/Drm.Server.Tests/AdminFilesApiTests.cs` with this test:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminFilesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-files-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminFilesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Group_grant_allows_group_member_to_view_file()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View"
        });
        await client.PostAsJsonAsync("/api/admin/groups", new { tenantId, groupId, name = "Finance" });
        await client.PostAsJsonAsync($"/api/admin/groups/{groupId}/members", new { tenantId, userId = memberUserId });

        using var grant = await client.PostAsJsonAsync($"/api/admin/files/{fileId}/grants", new
        {
            tenantId,
            subjectType = "Group",
            subjectId = groupId,
            permissions = "View"
        });

        grant.StatusCode.Should().Be(HttpStatusCode.Created);

        using var decide = await client.PostAsJsonAsync("/api/policy/decide", new
        {
            tenantId,
            fileId,
            userId = memberUserId,
            deviceId = Guid.NewGuid(),
            requestedPermission = "View"
        });

        var decision = await decide.Content.ReadFromJsonAsync<PolicyDecisionResponse>();
        decision!.Allowed.Should().BeTrue();
        decision.ReasonCode.Should().Be("allowed");
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record PolicyDecisionResponse(bool Allowed, string AllowedPermissions, string ReasonCode, string? WatermarkTemplate);
}
```

- [ ] **Step 2: Update file registration to create owner grant**

In `FilesEndpoints.RegisterFileAsync`, after `dbContext.ProtectedFiles.Add(file);`, add:

```csharp
dbContext.FileGrants.Add(new FileGrantEntity
{
    TenantId = file.TenantId,
    FileId = file.Id,
    SubjectType = "User",
    SubjectId = file.OwnerUserId,
    Permissions = file.Permissions.ToString(),
    CreatedAtUtc = DateTimeOffset.UtcNow
});
```

- [ ] **Step 3: Add admin file grant endpoint**

Create `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs`:

```csharp
using Drm.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminFilesEndpoints
{
    public static IEndpointRouteBuilder MapAdminFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/files");
        group.MapPost("/{fileId:guid}/grants", UpsertGrantAsync);
        group.MapGet("/", SearchFilesAsync);
        return endpoints;
    }

    private static async Task<Results<Created<FileGrantResponse>, BadRequest<ErrorResponse>, NotFound>> UpsertGrantAsync(
        Guid fileId,
        UpsertGrantRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GrantSubjectType>(request.SubjectType, ignoreCase: true, out var subjectType))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_subject_type"));
        }

        if (!PermissionParser.TryParse(request.Permissions, out var permissions))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_permissions"));
        }

        var fileExists = await dbContext.ProtectedFiles
            .AnyAsync(file => file.TenantId == request.TenantId && file.Id == fileId, cancellationToken);
        if (!fileExists)
        {
            return TypedResults.NotFound();
        }

        var subject = subjectType.ToString();
        var grant = await dbContext.FileGrants
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.FileId == fileId &&
                candidate.SubjectType == subject &&
                candidate.SubjectId == request.SubjectId,
                cancellationToken);

        if (grant is null)
        {
            grant = new FileGrantEntity
            {
                TenantId = request.TenantId,
                FileId = fileId,
                SubjectType = subject,
                SubjectId = request.SubjectId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.FileGrants.Add(grant);
        }

        grant.Permissions = permissions.ToString();
        dbContext.AuditEvents.Add(AdminAudit.PermissionEvent(request.TenantId, fileId, null, "file_grant_upserted"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/files/{fileId}/grants/{grant.SubjectType}/{grant.SubjectId}", FileGrantResponse.From(grant));
    }

    private static async Task<IReadOnlyList<FileSearchResponse>> SearchFilesAsync(
        Guid tenantId,
        string? q,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProtectedFiles.AsNoTracking().Where(file => file.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(file => file.ContentType.Contains(q));
        }

        return await query
            .OrderBy(file => file.Id)
            .Take(100)
            .Select(file => new FileSearchResponse(file.TenantId, file.Id, file.OwnerUserId, file.ContentType, file.ExpiresAtUtc, file.Revoked))
            .ToListAsync(cancellationToken);
    }

    private sealed record UpsertGrantRequest(Guid TenantId, string SubjectType, Guid SubjectId, string Permissions);
    private sealed record ErrorResponse(string ReasonCode);
    private sealed record FileGrantResponse(Guid TenantId, Guid FileId, string SubjectType, Guid SubjectId, string Permissions)
    {
        public static FileGrantResponse From(FileGrantEntity grant)
            => new(grant.TenantId, grant.FileId, grant.SubjectType, grant.SubjectId, grant.Permissions);
    }
    private sealed record FileSearchResponse(Guid TenantId, Guid FileId, Guid OwnerUserId, string ContentType, DateTimeOffset ExpiresAtUtc, bool Revoked);
}
```

Modify `Program.cs`:

```csharp
app.MapAdminFilesEndpoints();
```

- [ ] **Step 4: Update policy endpoint grant loading**

In `PolicyEndpoints.DecideAsync`, replace the single owner grant construction with:

```csharp
var userGroupIds = await dbContext.GroupMembers
    .AsNoTracking()
    .Where(member => member.TenantId == request.TenantId && member.UserId == request.UserId)
    .Select(member => member.GroupId)
    .ToListAsync(cancellationToken);

var fileGrants = await dbContext.FileGrants
    .AsNoTracking()
    .Where(grant => grant.TenantId == request.TenantId && grant.FileId == request.FileId)
    .ToListAsync(cancellationToken);

var effectivePermissions = Permission.None;
foreach (var grant in fileGrants)
{
    var applies =
        grant.SubjectType == "User" && grant.SubjectId == request.UserId ||
        grant.SubjectType == "Group" && userGroupIds.Contains(grant.SubjectId);

    if (applies && PermissionParser.TryParse(grant.Permissions, out var parsed))
    {
        effectivePermissions |= parsed;
    }
}
```

Build the policy with one grant for the requesting user:

```csharp
Grants: [new FileGrant(new UserId(request.UserId), effectivePermissions)]
```

If `effectivePermissions == Permission.None`, this should naturally deny `permission_not_granted` for a known file.

- [ ] **Step 5: Verify and commit**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Registering_file_creates_owner_file_grant|Group_grant_allows_group_member_to_view_file"
/Users/pop7/.dotnet/dotnet test Drm.sln
```

Commit:

```bash
git add src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: authorize files with explicit grants"
```

## Task 5: Add File Search and Bulk Grant Updates

**Files:**
- Modify: `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs`
- Modify: `tests/Drm.Server.Tests/AdminFilesApiTests.cs`

- [ ] **Step 1: Add tests**

Add to `AdminFilesApiTests`:

```csharp
[Fact]
public async Task Admin_can_search_files_by_tenant()
{
    using var client = factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var otherTenantId = Guid.NewGuid();

    await client.PostAsJsonAsync("/api/files", new
    {
        tenantId,
        fileId = Guid.NewGuid(),
        ownerUserId = Guid.NewGuid(),
        contentType = "application/pdf",
        expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        permissions = "View"
    });
    await client.PostAsJsonAsync("/api/files", new
    {
        tenantId = otherTenantId,
        fileId = Guid.NewGuid(),
        ownerUserId = Guid.NewGuid(),
        contentType = "application/pdf",
        expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        permissions = "View"
    });

    var files = await client.GetFromJsonAsync<List<FileSearchResponse>>($"/api/admin/files?tenantId={tenantId}&q=pdf");

    files.Should().ContainSingle(file => file.TenantId == tenantId);
}

[Fact]
public async Task Admin_can_bulk_replace_file_grants()
{
    using var client = factory.CreateClient();
    var tenantId = Guid.NewGuid();
    var fileId = Guid.NewGuid();
    var firstUser = Guid.NewGuid();
    var secondUser = Guid.NewGuid();

    await client.PostAsJsonAsync("/api/files", new
    {
        tenantId,
        fileId,
        ownerUserId = firstUser,
        contentType = "application/pdf",
        expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        permissions = "View"
    });

    using var replace = await client.PutAsJsonAsync($"/api/admin/files/{fileId}/grants", new
    {
        tenantId,
        grants = new[]
        {
            new { subjectType = "User", subjectId = secondUser, permissions = "View, Print" }
        }
    });

    replace.StatusCode.Should().Be(HttpStatusCode.OK);

    using var firstDecision = await client.PostAsJsonAsync("/api/policy/decide", new
    {
        tenantId,
        fileId,
        userId = firstUser,
        deviceId = Guid.NewGuid(),
        requestedPermission = "View"
    });
    using var secondDecision = await client.PostAsJsonAsync("/api/policy/decide", new
    {
        tenantId,
        fileId,
        userId = secondUser,
        deviceId = Guid.NewGuid(),
        requestedPermission = "Print"
    });

    (await firstDecision.Content.ReadFromJsonAsync<PolicyDecisionResponse>())!.Allowed.Should().BeFalse();
    (await secondDecision.Content.ReadFromJsonAsync<PolicyDecisionResponse>())!.Allowed.Should().BeTrue();
}

private sealed record FileSearchResponse(Guid TenantId, Guid FileId, Guid OwnerUserId, string ContentType, DateTimeOffset ExpiresAtUtc, bool Revoked);
```

- [ ] **Step 2: Implement bulk replace endpoint**

In `AdminFilesEndpoints.MapAdminFilesEndpoints`, add:

```csharp
group.MapPut("/{fileId:guid}/grants", ReplaceGrantsAsync);
```

Add:

```csharp
private static async Task<Results<Ok<IReadOnlyList<FileGrantResponse>>, BadRequest<ErrorResponse>, NotFound>> ReplaceGrantsAsync(
    Guid fileId,
    ReplaceGrantsRequest request,
    AppDbContext dbContext,
    CancellationToken cancellationToken)
{
    var fileExists = await dbContext.ProtectedFiles
        .AnyAsync(file => file.TenantId == request.TenantId && file.Id == fileId, cancellationToken);
    if (!fileExists)
    {
        return TypedResults.NotFound();
    }

    var parsed = new List<FileGrantEntity>();
    foreach (var grant in request.Grants)
    {
        if (!Enum.TryParse<GrantSubjectType>(grant.SubjectType, ignoreCase: true, out var subjectType))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_subject_type"));
        }
        if (!PermissionParser.TryParse(grant.Permissions, out var permissions))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_permissions"));
        }
        parsed.Add(new FileGrantEntity
        {
            TenantId = request.TenantId,
            FileId = fileId,
            SubjectType = subjectType.ToString(),
            SubjectId = grant.SubjectId,
            Permissions = permissions.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    var existing = await dbContext.FileGrants
        .Where(grant => grant.TenantId == request.TenantId && grant.FileId == fileId)
        .ToListAsync(cancellationToken);
    dbContext.FileGrants.RemoveRange(existing);
    dbContext.FileGrants.AddRange(parsed);
    dbContext.AuditEvents.Add(AdminAudit.PermissionEvent(request.TenantId, fileId, null, "file_grants_replaced"));
    await dbContext.SaveChangesAsync(cancellationToken);

    return TypedResults.Ok<IReadOnlyList<FileGrantResponse>>(parsed.Select(FileGrantResponse.From).ToList());
}

private sealed record ReplaceGrantsRequest(Guid TenantId, IReadOnlyList<ReplaceGrantItem> Grants);
private sealed record ReplaceGrantItem(string SubjectType, Guid SubjectId, string Permissions);
```

- [ ] **Step 3: Verify and commit**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter "Admin_can_search_files_by_tenant|Admin_can_bulk_replace_file_grants"
/Users/pop7/.dotnet/dotnet test Drm.sln
```

Commit:

```bash
git add src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: add admin file search and bulk grants"
```

## Task 6: Add Audit Filtering and CSV Export

**Files:**
- Create: `src/Drm.Server/Endpoints/AdminAuditEndpoints.cs`
- Create: `tests/Drm.Server.Tests/AdminAuditApiTests.cs`
- Modify: `src/Drm.Server/Program.cs`

- [ ] **Step 1: Write audit filtering and CSV tests**

Create `tests/Drm.Server.Tests/AdminAuditApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminAuditApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-audit-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminAuditApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_filter_audit_events_and_export_csv()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View"
        });

        var events = await client.GetFromJsonAsync<List<AuditResponse>>($"/api/admin/audit?tenantId={tenantId}&eventType=file_registered");
        events.Should().ContainSingle(e => e.EventType == "file_registered");

        var csv = await client.GetStringAsync($"/api/admin/audit.csv?tenantId={tenantId}&eventType=file_registered");
        csv.Should().StartWith("createdAtUtc,tenantId,fileId,userId,eventType,reasonCode");
        csv.Should().Contain("file_registered");
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed record AuditResponse(
        DateTimeOffset CreatedAtUtc,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode);
}
```

- [ ] **Step 2: Implement admin audit endpoints**

Create `src/Drm.Server/Endpoints/AdminAuditEndpoints.cs`:

```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminAuditEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/audit", ListAuditAsync);
        endpoints.MapGet("/api/admin/audit.csv", ExportAuditCsvAsync);
        return endpoints;
    }

    private static async Task<IReadOnlyList<AuditResponse>> ListAuditAsync(
        Guid tenantId,
        string? eventType,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await ApplyFilters(dbContext, tenantId, eventType)
            .Take(500)
            .Select(e => AuditResponse.From(e))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IResult> ExportAuditCsvAsync(
        Guid tenantId,
        string? eventType,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var events = await ApplyFilters(dbContext, tenantId, eventType)
            .Take(5000)
            .ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("createdAtUtc,tenantId,fileId,userId,eventType,reasonCode");
        foreach (var item in events)
        {
            csv.Append(item.CreatedAtUtc.ToString("O")).Append(',')
                .Append(item.TenantId).Append(',')
                .Append(item.FileId?.ToString() ?? "").Append(',')
                .Append(item.UserId?.ToString() ?? "").Append(',')
                .Append(Escape(item.EventType)).Append(',')
                .Append(Escape(item.ReasonCode)).AppendLine();
        }

        return Results.Text(csv.ToString(), "text/csv");
    }

    private static IQueryable<AuditEventEntity> ApplyFilters(AppDbContext dbContext, Guid tenantId, string? eventType)
    {
        var query = dbContext.AuditEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        return query.OrderByDescending(e => e.CreatedAtUtc);
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private sealed record AuditResponse(
        DateTimeOffset CreatedAtUtc,
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode)
    {
        public static AuditResponse From(AuditEventEntity e)
            => new(e.CreatedAtUtc, e.TenantId, e.FileId, e.UserId, e.EventType, e.ReasonCode);
    }
}
```

Modify `Program.cs`:

```csharp
app.MapAdminAuditEndpoints();
```

- [ ] **Step 3: Verify and commit**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter Admin_can_filter_audit_events_and_export_csv
/Users/pop7/.dotnet/dotnet test Drm.sln
```

Commit:

```bash
git add src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: add admin audit export"
```

## Task 7: Add SIEM Webhook Delivery

**Files:**
- Create: `src/Drm.Server/Endpoints/AdminSiemEndpoints.cs`
- Create: `tests/Drm.Server.Tests/AdminSiemWebhookTests.cs`
- Modify: `src/Drm.Server/Program.cs`
- Modify: server endpoints that create audit events to call dispatcher after save

- [ ] **Step 1: Add SIEM abstractions**

Create `src/Drm.Server/SiemDelivery.cs`:

```csharp
namespace Drm.Server;

public interface ISiemEventSink
{
    Task SendAsync(SiemWebhookEntity webhook, AuditEventEntity auditEvent, CancellationToken cancellationToken);
}

public sealed class HttpSiemEventSink(HttpClient httpClient) : ISiemEventSink
{
    public async Task SendAsync(SiemWebhookEntity webhook, AuditEventEntity auditEvent, CancellationToken cancellationToken)
    {
        if (!webhook.Enabled)
        {
            return;
        }

        using var response = await httpClient.PostAsJsonAsync(webhook.Url, new
        {
            auditEvent.TenantId,
            auditEvent.FileId,
            auditEvent.UserId,
            auditEvent.EventType,
            auditEvent.ReasonCode,
            auditEvent.CreatedAtUtc
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}

public interface ISiemDispatcher
{
    Task DispatchAsync(AuditEventEntity auditEvent, CancellationToken cancellationToken);
}

public sealed class SiemDispatcher(AppDbContext dbContext, ISiemEventSink sink) : ISiemDispatcher
{
    public async Task DispatchAsync(AuditEventEntity auditEvent, CancellationToken cancellationToken)
    {
        var webhooks = await dbContext.SiemWebhooks
            .Where(webhook => webhook.TenantId == auditEvent.TenantId && webhook.Enabled)
            .ToListAsync(cancellationToken);

        foreach (var webhook in webhooks)
        {
            await sink.SendAsync(webhook, auditEvent, cancellationToken);
        }
    }
}
```

Add required `using Microsoft.EntityFrameworkCore;` and `using System.Net.Http.Json;`.

Register in `Program.cs`:

```csharp
builder.Services.AddHttpClient<ISiemEventSink, HttpSiemEventSink>();
builder.Services.AddScoped<ISiemDispatcher, SiemDispatcher>();
```

- [ ] **Step 2: Add webhook endpoint**

Create `src/Drm.Server/Endpoints/AdminSiemEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminSiemEndpoints
{
    public static IEndpointRouteBuilder MapAdminSiemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/siem-webhooks");
        group.MapPost("/", CreateWebhookAsync);
        group.MapGet("/", ListWebhooksAsync);
        return endpoints;
    }

    private static async Task<Created<SiemWebhookResponse>> CreateWebhookAsync(
        CreateWebhookRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var webhook = new SiemWebhookEntity
        {
            TenantId = request.TenantId,
            WebhookId = request.WebhookId,
            Url = request.Url,
            Enabled = request.Enabled,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.SiemWebhooks.Add(webhook);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "siem_webhook_created"));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/siem-webhooks/{webhook.WebhookId}", SiemWebhookResponse.From(webhook));
    }

    private static async Task<IReadOnlyList<SiemWebhookResponse>> ListWebhooksAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.SiemWebhooks
            .AsNoTracking()
            .Where(webhook => webhook.TenantId == tenantId)
            .OrderBy(webhook => webhook.CreatedAtUtc)
            .Select(webhook => SiemWebhookResponse.From(webhook))
            .ToListAsync(cancellationToken);
    }

    private sealed record CreateWebhookRequest(Guid TenantId, Guid WebhookId, string Url, bool Enabled);
    private sealed record SiemWebhookResponse(Guid TenantId, Guid WebhookId, string Url, bool Enabled)
    {
        public static SiemWebhookResponse From(SiemWebhookEntity webhook)
            => new(webhook.TenantId, webhook.WebhookId, webhook.Url, webhook.Enabled);
    }
}
```

Modify `Program.cs`:

```csharp
app.MapAdminSiemEndpoints();
```

- [ ] **Step 3: Dispatch audit events from file registration**

In `FilesEndpoints.RegisterFileAsync`, inject `ISiemDispatcher dispatcher`. Assign the audit event to a variable:

```csharp
var auditEvent = new AuditEventEntity { ... };
dbContext.AuditEvents.Add(auditEvent);
```

After `SaveChangesAsync`, call:

```csharp
await dispatcher.DispatchAsync(auditEvent, cancellationToken);
```

Repeat only for `RevokeFileAsync` in this task. Other event sources can be wired in later tasks.

- [ ] **Step 4: Test SIEM dispatch**

Create `tests/Drm.Server.Tests/AdminSiemWebhookTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

public sealed class AdminSiemWebhookTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-siem-{Guid.NewGuid():N}.db");
    private readonly RecordingSiemSink sink = new();
    private readonly WebApplicationFactory<Program> factory;

    public AdminSiemWebhookTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<ISiemEventSink>(sink);
                });
            });
    }

    [Fact]
    public async Task Enabled_siem_webhook_receives_file_registered_event()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var webhook = await client.PostAsJsonAsync("/api/admin/siem-webhooks", new
        {
            tenantId,
            webhookId = Guid.NewGuid(),
            url = "https://siem.example/events",
            enabled = true
        });
        webhook.StatusCode.Should().Be(HttpStatusCode.Created);

        using var register = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId = Guid.NewGuid(),
            ownerUserId = Guid.NewGuid(),
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        sink.Events.Should().ContainSingle(e => e.EventType == "file_registered" && e.TenantId == tenantId);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private sealed class RecordingSiemSink : ISiemEventSink
    {
        public List<AuditEventEntity> Events { get; } = [];

        public Task SendAsync(SiemWebhookEntity webhook, AuditEventEntity auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 5: Verify and commit**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter Enabled_siem_webhook_receives_file_registered_event
/Users/pop7/.dotnet/dotnet test Drm.sln
```

Commit:

```bash
git add src/Drm.Server tests/Drm.Server.Tests
git commit -m "feat: add siem webhook dispatch"
```

## Task 8: Update Documentation and Final Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README**

Add a new section to `README.md`:

```markdown
## Phase 2A Admin and Audit APIs

The server includes admin APIs for local enterprise administration:

- `POST /api/admin/users`
- `GET /api/admin/users?tenantId=...`
- `POST /api/admin/groups`
- `POST /api/admin/groups/{groupId}/members`
- `POST /api/admin/policy-templates`
- `GET /api/admin/policy-templates?tenantId=...`
- `GET /api/admin/files?tenantId=...&q=...`
- `POST /api/admin/files/{fileId}/grants`
- `PUT /api/admin/files/{fileId}/grants`
- `GET /api/admin/audit?tenantId=...&eventType=...`
- `GET /api/admin/audit.csv?tenantId=...&eventType=...`
- `POST /api/admin/siem-webhooks`

Identity-provider integrations such as AD, Entra ID, SAML/OIDC, and SCIM are intentionally deferred to the next phase.
```

- [ ] **Step 2: Run final verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```

Expected: all tests and builds pass.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: document phase 2a admin APIs"
```

## Self-Review Notes

- This plan intentionally avoids identity-provider integrations because each provider requires configuration, protocol-specific tests, and deployment setup.
- The main architectural change is replacing owner-only authorization with explicit file grants. This is required before policy templates, bulk permission changes, and external sharing can be meaningful.
- SIEM delivery is deliberately abstraction-first so tests do not call external services.
- This phase still does not implement a browser admin console UI. It creates stable APIs that a UI can consume.

