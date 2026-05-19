using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminUsageTests : IDisposable
{
    private const string AdminApiKey = "usage-test-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-usage-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminUsageTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
            });
    }

    [Fact]
    public async Task Usage_returns_empty_on_fresh_database()
    {
        using var client = MakeAdminClient();
        using var response = await client.GetAsync("/api/admin/usage");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<UsageRow>>();
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Usage_reflects_tenant_user_and_key_counts()
    {
        using var client = MakeAdminClient();

        var tenantId = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"usage-{tenantId:N}", tenantId });

        // Create 2 users
        for (var i = 0; i < 2; i++)
        {
            client.DefaultRequestHeaders.Remove("X-DRM-Tenant-Id");
            client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
            await client.PostAsJsonAsync("/api/admin/users", new
            {
                tenantId,
                userId = Guid.NewGuid(),
                email = $"u{i}@{Guid.NewGuid():N}.com",
                displayName = $"User {i}"
            });
        }
        client.DefaultRequestHeaders.Remove("X-DRM-Tenant-Id");

        // Create 1 client key
        await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/client-keys", new { label = "test" });

        using var response = await client.GetAsync("/api/admin/usage");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await response.Content.ReadFromJsonAsync<List<UsageRow>>();
        var row = rows!.FirstOrDefault(r => r.TenantId == tenantId);
        row.Should().NotBeNull();
        row!.UsedSeats.Should().Be(2);
        row.ActiveKeys.Should().Be(1);
    }

    [Fact]
    public async Task Usage_csv_returns_text_csv_content_type()
    {
        using var client = MakeAdminClient();
        using var response = await client.GetAsync("/api/admin/usage?format=csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    [Fact]
    public async Task Usage_csv_contains_header_row()
    {
        using var client = MakeAdminClient();
        using var response = await client.GetAsync("/api/admin/usage?format=csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().StartWith("TenantId,Name,DisplayName,Status,UsedSeats,MaxSeats,ActiveKeys,ProtectedFiles,SnapshotAtUtc");
    }

    [Fact]
    public async Task Usage_csv_includes_tenant_row()
    {
        using var client = MakeAdminClient();
        var tenantId = Guid.NewGuid();
        var name = $"csvt-{tenantId:N}";
        await client.PostAsJsonAsync("/api/admin/tenants",
            new { name, tenantId, maxEncrypters = 10 });

        using var response = await client.GetAsync("/api/admin/usage?format=csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain(tenantId.ToString());
        csv.Should().Contain(name);
        csv.Should().Contain("Active");
        csv.Should().Contain(",10,");
    }

    [Fact]
    public async Task Usage_requires_admin_auth()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/admin/usage");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient MakeAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        return client;
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record UsageRow(
        Guid TenantId,
        string Name,
        string DisplayName,
        int Status,
        int UsedSeats,
        int? MaxSeats,
        int ActiveKeys,
        int ProtectedFiles,
        DateTimeOffset SnapshotAtUtc);
}
