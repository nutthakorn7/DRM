using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// Integration tests for v1.5 features:
///   1. File expiry — PATCH /api/files/{id}/expiry
///   2. Audit export — ?from/to/limit filters + GET /api/audit.csv (tenant-scoped)
///   3. Admin metrics — GET /api/admin/metrics
///   4. Operator alerts — CRUD /api/admin/alert-rules + GET /api/admin/alerts-fired
/// </summary>
public sealed class V15FeatureTests : IDisposable
{
    private const string AdminKey = "v15-test-key";
    private const string TenantKey = "v15-tenant-key";

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"drm-v15-{Guid.NewGuid():N}.db");

    private readonly WebApplicationFactory<Program> factory;

    public V15FeatureTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminKey);
            });
    }

    public void Dispose()
    {
        factory.Dispose();
        if (File.Exists(databasePath)) File.Delete(databasePath);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private HttpClient AdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminKey);
        return client;
    }

    private async Task<(Guid tenantId, Guid userId)> SeedTenantUserAsync(string name = "v15tenant")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            TenantId = tenantId,
            Name = name + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "V15 Tenant",
            Status = TenantStatus.Active,
            MaxEncrypters = 5,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = "user@v15.example",
            DisplayName = "V15 User",
            Active = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (tenantId, userId);
    }

    private async Task<Guid> SeedFileAsync(Guid tenantId, Guid userId, DateTimeOffset expiresAt)
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
            ExpiresAtUtc = expiresAt,
            Revoked = false,
            Permissions = Domain.Permission.View,
            WatermarkTemplate = "",
            OfflineLeaseMinutes = 15
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. File Expiry — PATCH /api/files/{id}/expiry
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchExpiry_updates_expiry_for_existing_file()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId, DateTimeOffset.UtcNow.AddDays(7));
        var newExpiry = DateTimeOffset.UtcNow.AddDays(30);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        using var response = await client.PatchAsJsonAsync(
            $"/api/files/{fileId}/expiry?tenantId={tenantId}",
            new { expiresAtUtc = newExpiry });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.ProtectedFiles.FindAsync(tenantId, fileId);
        file!.ExpiresAtUtc.Should().BeCloseTo(newExpiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task PatchExpiry_returns_404_for_unknown_file()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        using var response = await client.PatchAsJsonAsync(
            $"/api/files/{Guid.NewGuid()}/expiry?tenantId={tenantId}",
            new { expiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchExpiry_returns_400_for_revoked_file()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId, DateTimeOffset.UtcNow.AddDays(1));

        // Revoke the file
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.ProtectedFiles.FindAsync(tenantId, fileId);
        file!.Revoked = true;
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        using var response = await client.PatchAsJsonAsync(
            $"/api/files/{fileId}/expiry?tenantId={tenantId}",
            new { expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("file_revoked");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Audit export — ?from/to/limit filters
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminAudit_limit_param_caps_result_count()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = tenantId,
                EventType = "file.view",
                ReasonCode = "ok",
                CreatedAtUtc = now.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        using var client = AdminClient();
        var rows = await client.GetFromJsonAsync<List<AuditEventRow>>(
            $"/api/admin/audit?tenantId={tenantId}&limit=3");

        rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task AdminAudit_from_filter_excludes_older_events()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId, EventType = "file.view", ReasonCode = "old",
            CreatedAtUtc = cutoff.AddDays(-5)
        });
        db.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId, EventType = "file.view", ReasonCode = "new",
            CreatedAtUtc = cutoff.AddHours(1)
        });
        await db.SaveChangesAsync();

        using var client = AdminClient();
        var rows = await client.GetFromJsonAsync<List<AuditEventRow>>(
            $"/api/admin/audit?tenantId={tenantId}&from={cutoff:O}");

        rows.Should().HaveCount(1);
        rows![0].ReasonCode.Should().Be("new");
    }

    [Fact]
    public async Task AdminAudit_csv_returns_text_csv_content_type()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId, EventType = "file.view", ReasonCode = "ok",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = AdminClient();
        using var response = await client.GetAsync(
            $"/api/admin/audit.csv?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("createdAtUtc,tenantId");
        csv.Should().Contain("file.view");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Admin Metrics
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminMetrics_returns_summary_and_buckets_for_default_period()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        _ = await SeedFileAsync(tenantId, userId, DateTimeOffset.UtcNow.AddDays(30));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId,
            EventType = "file.view",
            ReasonCode = "ok",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        using var client = AdminClient();
        var res = await client.GetFromJsonAsync<MetricsResponse>("/api/admin/metrics");

        res.Should().NotBeNull();
        res!.PeriodDays.Should().Be(30); // default is now 30
        res.Summary.TotalTenants.Should().BeGreaterThanOrEqualTo(1);
        res.Buckets.Should().HaveCount(30);
    }

    [Fact]
    public async Task AdminMetrics_rejects_invalid_period()
    {
        using var client = AdminClient();
        using var response = await client.GetAsync("/api/admin/metrics?period=999");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdminMetrics_requires_auth()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/admin/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. Operator Alert Rules
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlertRules_create_list_delete_lifecycle()
    {
        using var client = AdminClient();

        // Create (condition 1 = FilesProtectedAbove, no tenantId required)
        using var createRes = await client.PostAsJsonAsync("/api/admin/alert-rules", new
        {
            name = "High file count",
            condition = 1,
            threshold = 1000.0,
            tenantId = (Guid?)null,
            enabled = true,
            fireWebhook = false
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<AlertRuleRow>();
        created!.Name.Should().Be("High file count");
        created.Condition.Should().Be(1);
        created.Threshold.Should().Be(1000.0);
        created.Enabled.Should().BeTrue();

        // List
        var list = await client.GetFromJsonAsync<List<AlertRuleRow>>("/api/admin/alert-rules");
        list.Should().Contain(r => r.RuleId == created.RuleId);

        // Delete
        using var delRes = await client.DeleteAsync($"/api/admin/alert-rules/{created.RuleId}");
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfter = await client.GetFromJsonAsync<List<AlertRuleRow>>("/api/admin/alert-rules");
        listAfter.Should().NotContain(r => r.RuleId == created.RuleId);
    }

    [Fact]
    public async Task AlertRules_patch_updates_name_and_threshold()
    {
        using var client = AdminClient();

        using var createRes = await client.PostAsJsonAsync("/api/admin/alert-rules", new
        {
            name = "Original name",
            condition = 3, // NewRegistrationsAbove, no tenantId required
            threshold = 50.0,
            enabled = true,
            fireWebhook = false
        });
        var created = await createRes.Content.ReadFromJsonAsync<AlertRuleRow>();

        using var patchRes = await client.PatchAsJsonAsync(
            $"/api/admin/alert-rules/{created!.RuleId}",
            new { name = "Updated name", threshold = 99.5 });
        patchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patchRes.Content.ReadFromJsonAsync<AlertRuleRow>();
        updated!.Name.Should().Be("Updated name");
        updated.Threshold.Should().Be(99.5);
    }

    [Fact]
    public async Task AlertRules_create_requires_name()
    {
        using var client = AdminClient();
        using var res = await client.PostAsJsonAsync("/api/admin/alert-rules", new
        {
            name = "",
            condition = 0,
            threshold = 80.0
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AlertRules_create_rejects_invalid_condition()
    {
        using var client = AdminClient();
        using var res = await client.PostAsJsonAsync("/api/admin/alert-rules", new
        {
            name = "bad",
            condition = 999,
            threshold = 80.0
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AlertRules_require_auth()
    {
        using var client = factory.CreateClient();
        using var res = await client.GetAsync("/api/admin/alert-rules");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AlertsFired_returns_empty_list_when_none_exist()
    {
        using var client = AdminClient();
        var fired = await client.GetFromJsonAsync<List<AlertFiredRow>>("/api/admin/alerts-fired");
        fired.Should().NotBeNull();
        fired.Should().BeEmpty();
    }

    [Fact]
    public async Task AlertsFired_filters_by_ruleId()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule1 = Guid.NewGuid();
        var rule2 = Guid.NewGuid();
        db.OperatorAlertsFired.AddRange(
            new OperatorAlertFiredEntity { RuleId = rule1, RuleName = "Rule1", ActualValue = 90, Threshold = 80, FiredAtUtc = DateTimeOffset.UtcNow },
            new OperatorAlertFiredEntity { RuleId = rule2, RuleName = "Rule2", ActualValue = 60, Threshold = 50, FiredAtUtc = DateTimeOffset.UtcNow }
        );
        await db.SaveChangesAsync();

        using var client = AdminClient();
        var fired = await client.GetFromJsonAsync<List<AlertFiredRow>>(
            $"/api/admin/alerts-fired?ruleId={rule1}");

        fired.Should().HaveCount(1);
        fired![0].RuleName.Should().Be("Rule1");
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs for deserialization
    // ─────────────────────────────────────────────────────────────────────

    private sealed record AuditEventRow(
        long Id, Guid TenantId, Guid? FileId, Guid? UserId,
        string EventType, string ReasonCode, DateTimeOffset CreatedAtUtc);

    private sealed record MetricsResponse(
        int PeriodDays,
        MetricsSummary Summary,
        List<DayBucket> Buckets);

    private sealed record MetricsSummary(
        int TotalTenants, int ActiveTenants, int TotalUsers,
        long TotalFiles, int NewRegistrations, int ApprovedRegistrations);

    private sealed record DayBucket(
        string Date, int TotalEvents, int FilesProtected,
        int Decryptions, int NewUsers);

    private sealed record AlertRuleRow(
        Guid RuleId, string Name, Guid? TenantId, int Condition,
        string ConditionName, double Threshold, bool Enabled,
        bool FireWebhook, DateTimeOffset CreatedAtUtc);

    private sealed record AlertFiredRow(
        long Id, Guid RuleId, Guid? TenantId, string RuleName,
        double ActualValue, double Threshold, DateTimeOffset FiredAtUtc);

    private sealed record RegistrationStatus(string Value);
}
