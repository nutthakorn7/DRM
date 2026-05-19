using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// Integration tests for v1.6 features:
///   1. File access requests — POST /api/files/{id}/request-access
///   2. Access request review — PATCH /api/admin/access-requests/{id}/approve|reject
///   3. Tenant plan management — GET/PUT /api/admin/tenants/{id}/plan
///   4. Plan quota enforcement — file protect returns 400 when MaxFiles exceeded
///   5. Audit chain verification — GET /api/admin/audit/chain/verify
/// </summary>
public sealed class V16FeatureTests : IDisposable
{
    private const string AdminKey = "v16-test-key";

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"drm-v16-{Guid.NewGuid():N}.db");

    private readonly WebApplicationFactory<Program> factory;

    public V16FeatureTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminKey);
                builder.UseSetting("Drm:Security:AuditChainKey",
                    "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20");
            });
    }

    public void Dispose()
    {
        factory.Dispose();
        if (File.Exists(databasePath)) File.Delete(databasePath);
    }

    private HttpClient AdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminKey);
        return client;
    }

    private async Task<(Guid tenantId, Guid userId)> SeedTenantUserAsync(string name = "v16tenant")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            TenantId = tenantId,
            Name = name + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "V16 Tenant",
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = "user@v16.example",
            DisplayName = "V16 User",
            Active = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (tenantId, userId);
    }

    private async Task<Guid> SeedFileAsync(Guid tenantId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileId = Guid.NewGuid();
        db.ProtectedFiles.Add(new ProtectedFileEntity
        {
            Id = fileId,
            TenantId = tenantId,
            OwnerUserId = userId,
            ContentType = "application/pdf",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            Revoked = false,
            Permissions = Domain.Permission.View,
            WatermarkTemplate = "",
            OfflineLeaseMinutes = 15
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1 & 2. File access requests
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestAccess_creates_pending_request()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsJsonAsync(
            $"/api/files/{fileId}/request-access?tenantId={tenantId}",
            new { requesterEmail = "newuser@example.com", message = "Please grant access" });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<RequestCreatedBody>();
        body!.RequestId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RequestAccess_deduplicates_pending_requests()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var body = new { requesterEmail = "dup@example.com", message = "" };
        var r1 = await client.PostAsJsonAsync($"/api/files/{fileId}/request-access?tenantId={tenantId}", body);
        r1.StatusCode.Should().Be(HttpStatusCode.Created);

        var r2 = await client.PostAsJsonAsync($"/api/files/{fileId}/request-access?tenantId={tenantId}", body);
        r2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AdminApproveRequest_sets_status_and_creates_grant()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        // Seed requester user (so approve can find them and create a grant)
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var requesterId = Guid.NewGuid();
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId, UserId = requesterId,
            Email = "requester@example.com", DisplayName = "Requester",
            Active = true, CreatedAtUtc = DateTimeOffset.UtcNow
        });
        var req = new FileAccessRequestEntity
        {
            RequestId = Guid.NewGuid(), TenantId = tenantId, FileId = fileId,
            RequesterEmail = "requester@example.com", Message = "please",
            Status = AccessRequestStatus.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        db.FileAccessRequests.Add(req);
        await db.SaveChangesAsync();

        using var client = AdminClient();
        var res = await client.PatchAsync($"/api/admin/access-requests/{req.RequestId}/approve", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify grant was created
        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var grant = db2.FileGrants.FirstOrDefault(
            g => g.TenantId == tenantId && g.FileId == fileId && g.SubjectType == "user" && g.SubjectId == requesterId);
        grant.Should().NotBeNull();
    }

    [Fact]
    public async Task AdminRejectRequest_sets_rejected_status()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = new FileAccessRequestEntity
        {
            RequestId = Guid.NewGuid(), TenantId = tenantId, FileId = fileId,
            RequesterEmail = "reject@example.com", Message = "",
            Status = AccessRequestStatus.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        db.FileAccessRequests.Add(req);
        await db.SaveChangesAsync();

        using var client = AdminClient();
        var res = await client.PatchAsync($"/api/admin/access-requests/{req.RequestId}/reject", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<AccessRequestRow>();
        body!.Status.Should().Be("rejected");
    }

    [Fact]
    public async Task AdminListAccessRequests_returns_pending_filter()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FileAccessRequests.Add(new FileAccessRequestEntity
        {
            RequestId = Guid.NewGuid(), TenantId = tenantId, FileId = fileId,
            RequesterEmail = "a@example.com", Status = AccessRequestStatus.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow
        });
        db.FileAccessRequests.Add(new FileAccessRequestEntity
        {
            RequestId = Guid.NewGuid(), TenantId = tenantId, FileId = fileId,
            RequesterEmail = "b@example.com", Status = AccessRequestStatus.Approved,
            RequestedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = AdminClient();
        var rows = await client.GetFromJsonAsync<List<AccessRequestRow>>(
            $"/api/admin/access-requests?tenantId={tenantId}&status=pending");
        rows.Should().HaveCount(1);
        rows![0].RequesterEmail.Should().Be("a@example.com");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3 & 4. Tenant plan management + quota enforcement
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlan_returns_default_free_when_no_plan_set()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        var plan = await client.GetFromJsonAsync<PlanRow>($"/api/admin/tenants/{tenantId}/plan");
        plan!.Tier.Should().Be("free");
        plan.MaxFiles.Should().Be(50);
    }

    [Fact]
    public async Task PutPlan_upserts_plan_and_returns_updated_values()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        var res = await client.PutAsJsonAsync($"/api/admin/tenants/{tenantId}/plan",
            new { tier = "starter", maxFiles = 200, maxStorageMb = 2000 });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await res.Content.ReadFromJsonAsync<PlanRow>();
        plan!.Tier.Should().Be("starter");
        plan.MaxFiles.Should().Be(200);
    }

    [Fact]
    public async Task PutPlan_preset_applies_correct_limits()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        var res = await client.PutAsJsonAsync($"/api/admin/tenants/{tenantId}/plan",
            new { preset = "enterprise" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await res.Content.ReadFromJsonAsync<PlanRow>();
        plan!.Tier.Should().Be("enterprise");
        plan.MaxFiles.Should().BeNull();
    }

    [Fact]
    public async Task FileProtect_returns_400_when_plan_quota_exceeded()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();

        // Set plan with MaxFiles = 1
        using var planScope = factory.Services.CreateScope();
        var db = planScope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantPlans.Add(new TenantPlanEntity
        {
            TenantId = tenantId,
            Tier = PlanTier.Free,
            MaxFiles = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // Seed one existing file (fills quota)
        await SeedFileAsync(tenantId, userId);

        // Attempt to protect a second file
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        var registerRes = await client.PostAsJsonAsync("/api/files", new
        {
            fileId = Guid.NewGuid(),
            tenantId,
            ownerUserId = userId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            permissions = "view",
            recipients = Array.Empty<object>()
        });

        registerRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await registerRes.Content.ReadAsStringAsync();
        body.Should().Contain("plan_quota_exceeded");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 5. Audit chain verification
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditChainVerify_returns_valid_true_for_empty_tenant()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.GetFromJsonAsync<ChainVerifyRow>(
            $"/api/admin/audit/chain/verify?tenantId={tenantId}");
        res!.Valid.Should().BeTrue();
        res.EventsChecked.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────

    private sealed record RequestCreatedBody(Guid RequestId);
    private sealed record AccessRequestRow(
        Guid RequestId, Guid TenantId, Guid FileId,
        string RequesterEmail, string Status, DateTimeOffset RequestedAtUtc);
    private sealed record PlanRow(
        Guid TenantId, string Tier, int? MaxFiles, int? MaxStorageMb,
        int CurrentFileCount, bool FilesQuotaExceeded);
    private sealed record ChainVerifyRow(Guid TenantId, bool Valid, long EventsChecked, string? FailureReason);
}
