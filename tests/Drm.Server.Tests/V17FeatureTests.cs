using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// Integration tests for v1.7 features:
///   1. File collections — CRUD + add/remove files + apply-policy
///   2. Batch file operations — batch-revoke + batch-expiry
///   3. Key rotation — config upsert + manual trigger + history
/// </summary>
public sealed class V17FeatureTests : IDisposable
{
    private const string AdminKey = "v17-test-key";

    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), $"drm-v17-{Guid.NewGuid():N}.db");

    private readonly WebApplicationFactory<Program> factory;

    public V17FeatureTests()
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

    private async Task<(Guid tenantId, Guid userId)> SeedTenantUserAsync(string name = "v17tenant")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new TenantEntity
        {
            TenantId = tenantId,
            Name = name + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "V17 Tenant",
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId,
            UserId = userId,
            Email = "user@v17.example",
            DisplayName = "V17 User",
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

    private async Task SeedFileKeyAsync(Guid tenantId, Guid fileId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FileKeys.Add(new FileKeyEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            WrappedKeyNonceBase64 = "AAAAAAAAAAAAAAAA",
            WrappedKeyCiphertextBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            WrappedKeyTagBase64 = "AAAAAAAAAAAAAAAAAAAAAA==",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. File collections
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCollection_returns_201_with_collection()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsJsonAsync("/api/admin/collections", new
        {
            tenantId,
            name = "Q3 Reports",
            description = "All Q3 documents"
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<CollectionRow>();
        body!.Name.Should().Be("Q3 Reports");
        body.FileCount.Should().Be(0);
    }

    [Fact]
    public async Task ListCollections_returns_tenant_collections()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        await client.PostAsJsonAsync("/api/admin/collections", new { tenantId, name = "Alpha", description = (string?)null });
        await client.PostAsJsonAsync("/api/admin/collections", new { tenantId, name = "Beta", description = (string?)null });

        var rows = await client.GetFromJsonAsync<List<CollectionRow>>(
            $"/api/admin/collections?tenantId={tenantId}");
        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddFilesToCollection_creates_items_and_deduplicates()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var createRes = await client.PostAsJsonAsync("/api/admin/collections",
            new { tenantId, name = "Test Coll", description = (string?)null });
        var coll = await createRes.Content.ReadFromJsonAsync<CollectionRow>();

        // Add file once
        var r1 = await client.PostAsJsonAsync(
            $"/api/admin/collections/{coll!.CollectionId}/files",
            new { tenantId, fileIds = new[] { fileId } });
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await r1.Content.ReadFromJsonAsync<AddedBody>();
        body1!.Added.Should().Be(1);

        // Adding same file again returns added=0 (deduplication)
        var r2 = await client.PostAsJsonAsync(
            $"/api/admin/collections/{coll.CollectionId}/files",
            new { tenantId, fileIds = new[] { fileId } });
        var body2 = await r2.Content.ReadFromJsonAsync<AddedBody>();
        body2!.Added.Should().Be(0);
    }

    [Fact]
    public async Task RemoveFileFromCollection_returns_204()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var createRes = await client.PostAsJsonAsync("/api/admin/collections",
            new { tenantId, name = "Coll", description = (string?)null });
        var coll = await createRes.Content.ReadFromJsonAsync<CollectionRow>();

        await client.PostAsJsonAsync($"/api/admin/collections/{coll!.CollectionId}/files",
            new { tenantId, fileIds = new[] { fileId } });

        var del = await client.DeleteAsync(
            $"/api/admin/collections/{coll.CollectionId}/files/{fileId}?tenantId={tenantId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCollection_removes_collection_and_items()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var createRes = await client.PostAsJsonAsync("/api/admin/collections",
            new { tenantId, name = "Doomed", description = (string?)null });
        var coll = await createRes.Content.ReadFromJsonAsync<CollectionRow>();

        await client.PostAsJsonAsync($"/api/admin/collections/{coll!.CollectionId}/files",
            new { tenantId, fileIds = new[] { fileId } });

        var del = await client.DeleteAsync(
            $"/api/admin/collections/{coll.CollectionId}?tenantId={tenantId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify items are gone from DB
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FileCollectionItems.Any(i => i.CollectionId == coll.CollectionId).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPolicy_updates_expiry_for_all_files_in_collection()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId1 = await SeedFileAsync(tenantId, userId);
        var fileId2 = await SeedFileAsync(tenantId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var createRes = await client.PostAsJsonAsync("/api/admin/collections",
            new { tenantId, name = "Policy Coll", description = (string?)null });
        var coll = await createRes.Content.ReadFromJsonAsync<CollectionRow>();

        await client.PostAsJsonAsync($"/api/admin/collections/{coll!.CollectionId}/files",
            new { tenantId, fileIds = new[] { fileId1, fileId2 } });

        var newExpiry = DateTimeOffset.UtcNow.AddDays(90);
        var applyRes = await client.PostAsJsonAsync(
            $"/api/admin/collections/{coll.CollectionId}/apply-policy",
            new { tenantId, expiresAtUtc = newExpiry });
        applyRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var applyBody = await applyRes.Content.ReadFromJsonAsync<UpdatedBody>();
        applyBody!.Updated.Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Batch file operations
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchRevoke_revokes_multiple_files()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId1 = await SeedFileAsync(tenantId, userId);
        var fileId2 = await SeedFileAsync(tenantId, userId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsJsonAsync("/api/admin/files/batch-revoke", new
        {
            tenantId,
            fileIds = new[] { fileId1, fileId2 }
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<BatchResultBody>();
        body!.Results.Should().HaveCount(2);
        body.Results.Should().AllSatisfy(r => r.Status.Should().Be("revoked"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ProtectedFiles.Where(f => f.TenantId == tenantId).All(f => f.Revoked).Should().BeTrue();
    }

    [Fact]
    public async Task BatchRevoke_returns_not_found_for_missing_file_ids()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var realFileId = await SeedFileAsync(tenantId, userId);
        var missingId = Guid.NewGuid();

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsJsonAsync("/api/admin/files/batch-revoke", new
        {
            tenantId,
            fileIds = new[] { realFileId, missingId }
        });

        var body = await res.Content.ReadFromJsonAsync<BatchResultBody>();
        body!.Results.Should().Contain(r => r.FileId == realFileId && r.Status == "revoked");
        body.Results.Should().Contain(r => r.FileId == missingId && r.Status == "not_found");
    }

    [Fact]
    public async Task BatchExpiry_updates_expiry_for_multiple_files()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId1 = await SeedFileAsync(tenantId, userId);
        var fileId2 = await SeedFileAsync(tenantId, userId);
        var newExpiry = DateTimeOffset.UtcNow.AddDays(60);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PostAsJsonAsync("/api/admin/files/batch-expiry", new
        {
            tenantId,
            fileIds = new[] { fileId1, fileId2 },
            expiresAtUtc = newExpiry
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<BatchResultBody>();
        body!.Results.Should().AllSatisfy(r => r.Status.Should().Be("updated"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var files = db.ProtectedFiles.Where(f => f.TenantId == tenantId).ToList();
        files.Should().AllSatisfy(f =>
            f.ExpiresAtUtc.Should().BeCloseTo(newExpiry, TimeSpan.FromSeconds(5)));
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Key rotation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetKeyRotationConfig_returns_defaults_when_not_configured()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var config = await client.GetFromJsonAsync<KeyRotationConfigRow>(
            $"/api/admin/tenants/{tenantId}/key-rotation");
        config!.Enabled.Should().BeFalse();
        config.IntervalDays.Should().Be(90);
    }

    [Fact]
    public async Task PutKeyRotationConfig_upserts_schedule()
    {
        var (tenantId, _) = await SeedTenantUserAsync();

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        var res = await client.PutAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/key-rotation",
            new { enabled = true, intervalDays = 30 });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<KeyRotationConfigRow>();
        body!.Enabled.Should().BeTrue();
        body.IntervalDays.Should().Be(30);
        body.NextRotationDueUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task TriggerKeyRotation_rotates_keys_and_records_history()
    {
        var (tenantId, userId) = await SeedTenantUserAsync();
        var fileId = await SeedFileAsync(tenantId, userId);
        await SeedFileKeyAsync(tenantId, fileId);

        using var client = AdminClient();
        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        // Configure schedule first so config exists
        await client.PutAsJsonAsync($"/api/admin/tenants/{tenantId}/key-rotation",
            new { enabled = true, intervalDays = 90 });

        var triggerRes = await client.PostAsync(
            $"/api/admin/tenants/{tenantId}/key-rotation/trigger", null);
        triggerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await triggerRes.Content.ReadFromJsonAsync<TriggerResultBody>();
        body!.FilesRotated.Should().Be(1);

        // History should have one entry
        var history = await client.GetFromJsonAsync<List<KeyRotationHistoryRow>>(
            $"/api/admin/tenants/{tenantId}/key-rotation/history");
        history.Should().HaveCount(1);
        history![0].TriggeredBy.Should().Be("manual");
        history[0].FilesRotated.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs
    // ─────────────────────────────────────────────────────────────────────

    private sealed record CollectionRow(
        Guid CollectionId, Guid TenantId, string Name,
        string? Description, int FileCount, DateTimeOffset CreatedAtUtc);

    private sealed record AddedBody(int Added);
    private sealed record UpdatedBody(int Updated);

    private sealed record BatchFileResultRow(Guid FileId, string Status);
    private sealed record BatchResultBody(List<BatchFileResultRow> Results);

    private sealed record KeyRotationConfigRow(
        Guid TenantId, bool Enabled, int IntervalDays,
        DateTimeOffset? LastRotatedAtUtc, DateTimeOffset? NextRotationDueUtc);

    private sealed record TriggerResultBody(Guid TenantId, int FilesRotated, DateTimeOffset RotatedAtUtc);

    private sealed record KeyRotationHistoryRow(
        long Id, Guid TenantId, int FilesRotated, string TriggeredBy, DateTimeOffset RotatedAtUtc);
}
