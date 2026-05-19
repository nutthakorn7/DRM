using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// Integration tests for v1.8 features:
///   1. Compliance export — ZIP bundle with manifest, files, audit events, chain verification
///   2. GDPR erasure — anonymize user, delete grants/requests, tombstone audit events
///   3. Data retention policy — GET/PUT policy, apply dry-run, apply deletes expired files
/// </summary>
public sealed class V18FeatureTests : IDisposable
{
    private const string AdminKey = "v18-test-key";
    private const string ChainKey = "0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20";

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"drm-v18-{Guid.NewGuid():N}.db");

    private readonly WebApplicationFactory<Program> factory;

    public V18FeatureTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminKey);
                builder.UseSetting("Drm:Security:AuditChainKey", ChainKey);
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

    private async Task<(Guid tenantId, Guid userId)> SeedTenantUserAsync(string prefix = "v18")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            TenantId = tenantId,
            Name = prefix + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "V18 Tenant",
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = $"user-{userId:N}@v18.example",
            DisplayName = "V18 User",
            Active = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (tenantId, userId);
    }

    private async Task<Guid> SeedFileAsync(Guid tenantId, Guid userId,
        DateTimeOffset? expiresAt = null)
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
            ExpiresAtUtc = expiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
            Revoked = false,
            Permissions = Domain.Permission.View,
            WatermarkTemplate = "",
            OfflineLeaseMinutes = 15
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private async Task SeedFileGrantAsync(Guid tenantId, Guid fileId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FileGrants.Add(new FileGrantEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            SubjectType = "user",
            SubjectId = userId,
            Permissions = "View",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAuditEventAsync(Guid tenantId, Guid fileId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = userId,
            EventType = "file_viewed",
            ReasonCode = "test",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAccessRequestAsync(Guid tenantId, Guid fileId, string requesterEmail)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FileAccessRequests.Add(new FileAccessRequestEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            RequesterEmail = requesterEmail,
            Status = AccessRequestStatus.Pending,
            RequestedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. Compliance export
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComplianceExport_returns_zip_with_all_required_entries()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsync(
            $"/api/admin/tenants/{tenantId}/compliance/export", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var bytes = await res.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var entryNames = zip.Entries.Select(e => e.Name).ToList();
        entryNames.Should().Contain("manifest.json");
        entryNames.Should().Contain("files.json");
        entryNames.Should().Contain("audit-events.json");
        entryNames.Should().Contain("chain-verification.json");
    }

    [Fact]
    public async Task ComplianceExport_files_json_contains_protected_file()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsync(
            $"/api/admin/tenants/{tenantId}/compliance/export", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var filesEntry = zip.GetEntry("files.json")!;
        using var stream = filesEntry.Open();
        var doc = await JsonDocument.ParseAsync(stream);
        var ids = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("Id").GetGuid())
            .ToList();
        ids.Should().Contain(fileId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. GDPR erasure
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GdprErasure_anonymizes_user_email_and_displayname()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/users/{userId}/data");

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.TenantUsers.Single(u => u.UserId == userId && u.TenantId == tenantId);
        user.Email.Should().StartWith("erased-");
        user.Email.Should().EndWith("@gdpr.invalid");
        user.DisplayName.Should().Be("Erased User");
        user.Active.Should().BeFalse();
    }

    [Fact]
    public async Task GdprErasure_removes_file_grants_for_user()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);
        await SeedFileGrantAsync(tenantId, fileId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/users/{userId}/data");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<EraseResultBody>();
        body!.GrantsDeleted.Should().Be(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = db.FileGrants
            .Where(g => g.TenantId == tenantId && g.SubjectId == userId)
            .ToList();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task GdprErasure_anonymizes_audit_events_for_user()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);
        await SeedAuditEventAsync(tenantId, fileId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/users/{userId}/data");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<EraseResultBody>();
        body!.AuditEventsAnonymized.Should().Be(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = db.AuditEvents
            .Where(e => e.TenantId == tenantId && e.FileId == fileId)
            .ToList();
        events.Should().OnlyContain(e => e.UserId == Guid.Empty);
    }

    [Fact]
    public async Task GdprErasure_deletes_access_requests_for_user_email()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        // Capture the email before seeding the request
        using var setupScope = factory.Services.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = setupDb.TenantUsers
            .Single(u => u.UserId == userId && u.TenantId == tenantId).Email;

        await SeedAccessRequestAsync(tenantId, fileId, email);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/users/{userId}/data");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<EraseResultBody>();
        body!.AccessRequestsDeleted.Should().Be(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = db.FileAccessRequests
            .Where(r => r.TenantId == tenantId && r.RequesterEmail == email)
            .ToList();
        remaining.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Data retention policy
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRetentionPolicy_returns_defaults_when_not_configured()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var policy = await client.GetFromJsonAsync<RetentionPolicyRow>(
            $"/api/admin/tenants/{tenantId}/retention-policy");

        policy!.TenantId.Should().Be(tenantId);
        policy.Enabled.Should().BeFalse();
        policy.FileRetentionDays.Should().BeNull();
    }

    [Fact]
    public async Task PutRetentionPolicy_upserts_policy()
    {
        var (tenantId, _) = await SeedTenantUserAsync();
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/retention-policy",
            new { enabled = true, fileRetentionDays = 90 });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy = await res.Content.ReadFromJsonAsync<RetentionPolicyRow>();
        policy!.Enabled.Should().BeTrue();
        policy.FileRetentionDays.Should().Be(90);

        // Idempotent update
        var res2 = await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/retention-policy",
            new { enabled = true, fileRetentionDays = 180 });
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy2 = await res2.Content.ReadFromJsonAsync<RetentionPolicyRow>();
        policy2!.FileRetentionDays.Should().Be(180);
    }

    [Fact]
    public async Task ApplyRetention_dry_run_counts_candidates_without_deleting()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();

        // Seed one expired file (expired 400 days ago)
        await SeedFileAsync(tenantId, userId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-400));
        // Seed one active file
        await SeedFileAsync(tenantId, userId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30));

        // Enable retention policy of 365 days
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/retention-policy",
            new { enabled = true, fileRetentionDays = 365 });

        var res = await client.PostAsync(
            $"/api/admin/tenants/{tenantId}/retention-policy/apply?dryRun=true", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<ApplyRetentionResultBody>();
        body!.DryRun.Should().BeTrue();
        body.FilesCandidates.Should().Be(1);
        body.FilesDeleted.Should().Be(0);

        // Verify file is still in DB
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ProtectedFiles.Count(f => f.TenantId == tenantId).Should().Be(2);
    }

    [Fact]
    public async Task ApplyRetention_deletes_expired_files_past_retention_period()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();

        // Seed one expired file (expired 400 days ago)
        await SeedFileAsync(tenantId, userId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-400));
        // Seed one active file
        await SeedFileAsync(tenantId, userId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30));

        // Enable retention policy of 365 days
        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/retention-policy",
            new { enabled = true, fileRetentionDays = 365 });

        var res = await client.PostAsync(
            $"/api/admin/tenants/{tenantId}/retention-policy/apply?dryRun=false", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<ApplyRetentionResultBody>();
        body!.DryRun.Should().BeFalse();
        body.FilesDeleted.Should().Be(1);

        // Only the active file should remain
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ProtectedFiles.Count(f => f.TenantId == tenantId).Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────

    private sealed record EraseResultBody(
        Guid UserId,
        int UsersAnonymized,
        int GrantsDeleted,
        int AuditEventsAnonymized,
        int AccessRequestsDeleted);

    private sealed record RetentionPolicyRow(
        Guid TenantId,
        bool Enabled,
        int? FileRetentionDays,
        DateTimeOffset? UpdatedAtUtc);

    private sealed record ApplyRetentionResultBody(
        Guid TenantId,
        bool DryRun,
        int FilesDeleted,
        int FilesCandidates);
}
