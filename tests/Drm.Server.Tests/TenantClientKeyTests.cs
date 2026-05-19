using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class TenantClientKeyTests : IDisposable
{
    private const string AdminApiKey = "client-key-test-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-client-keys-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public TenantClientKeyTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                // No global client key — so per-tenant keys are the only path
            });
    }

    // ── Key management ────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_key_returns_drm_tk_prefixed_secret()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);

        using var response = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/client-keys", new { label = "prod" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateKeyBody>();
        created!.Key.Should().StartWith("drm_tk_");
        created.TenantId.Should().Be(tenantId);
        created.Label.Should().Be("prod");
    }

    [Fact]
    public async Task List_keys_returns_active_keys_only()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);
        var k1 = await CreateKey(client, tenantId, "key-1");
        var k2 = await CreateKey(client, tenantId, "key-2");

        // Revoke k1
        await client.DeleteAsync($"/api/admin/tenants/{tenantId}/client-keys/{k1.KeyId}");

        using var response = await client.GetAsync($"/api/admin/tenants/{tenantId}/client-keys");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var keys = await response.Content.ReadFromJsonAsync<List<KeyBody>>();
        keys.Should().HaveCount(1);
        keys![0].KeyId.Should().Be(k2.KeyId);
    }

    [Fact]
    public async Task Revoke_key_returns_no_content()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);
        var created = await CreateKey(client, tenantId, "to-revoke");

        using var response = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/client-keys/{created.KeyId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Revoke_unknown_key_returns_not_found()
    {
        using var client = MakeAdminClient();
        var tenantId = await CreateTenant(client);

        using var response = await client.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/client-keys/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Authentication via per-tenant key ─────────────────────────────────────

    [Fact]
    public async Task Valid_tenant_key_authenticates_api_requests()
    {
        using var adminClient = MakeAdminClient();
        var tenantId = await CreateTenant(adminClient);

        // Create a user in that tenant first
        adminClient.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        await adminClient.PostAsJsonAsync("/api/admin/users", new
        {
            tenantId,
            userId = Guid.NewGuid(),
            email = $"u@{Guid.NewGuid():N}.com",
            displayName = "User"
        });
        adminClient.DefaultRequestHeaders.Remove("X-DRM-Tenant-Id");

        var created = await CreateKey(adminClient, tenantId, "auth-test");

        // Now use the per-tenant key as a client key (not admin key)
        using var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", created.Key);
        apiClient.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        // /api/files is a client endpoint — with the tenant key it should not get 401/403
        using var response = await apiClient.GetAsync($"/api/files?tenantId={tenantId}");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoked_tenant_key_is_rejected()
    {
        using var adminClient = MakeAdminClient();
        var tenantId = await CreateTenant(adminClient);
        var created = await CreateKey(adminClient, tenantId, "revoke-test");

        await adminClient.DeleteAsync(
            $"/api/admin/tenants/{tenantId}/client-keys/{created.KeyId}");

        using var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", created.Key);
        apiClient.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());

        using var response = await apiClient.GetAsync($"/api/files?tenantId={tenantId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_drm_tk_key_is_rejected_with_403()
    {
        using var apiClient = factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-DRM-Client-Key", "drm_tk_notarealkey");

        using var response = await apiClient.GetAsync("/api/files?tenantId=" + Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Seat usage in TenantResponse ──────────────────────────────────────────

    [Fact]
    public async Task Used_seats_reflects_user_count()
    {
        using var client = MakeAdminClient();
        var tenantId = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"seats-{tenantId:N}", tenantId });

        client.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        await client.PostAsJsonAsync("/api/admin/users", new
        {
            tenantId,
            userId = Guid.NewGuid(),
            email = $"u1@{Guid.NewGuid():N}.com",
            displayName = "User 1"
        });
        client.DefaultRequestHeaders.Remove("X-DRM-Tenant-Id");

        using var response = await client.GetAsync($"/api/admin/tenants/{tenantId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantBody>();
        body!.UsedSeats.Should().Be(1);
    }

    [Fact]
    public async Task List_tenants_includes_used_seats()
    {
        using var client = MakeAdminClient();
        var tenantId = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"list-seats-{tenantId:N}", tenantId });

        using var response = await client.GetAsync("/api/admin/tenants");
        var tenants = await response.Content.ReadFromJsonAsync<List<TenantBody>>();
        tenants.Should().Contain(t => t.TenantId == tenantId && t.UsedSeats == 0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient MakeAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        return client;
    }

    private async Task<Guid> CreateTenant(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        var r = await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"t-{tenantId:N}", tenantId });
        r.EnsureSuccessStatusCode();
        return tenantId;
    }

    private async Task<CreateKeyBody> CreateKey(HttpClient client, Guid tenantId, string label)
    {
        var r = await client.PostAsJsonAsync(
            $"/api/admin/tenants/{tenantId}/client-keys", new { label });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<CreateKeyBody>())!;
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record TenantBody(
        Guid TenantId, string Name, string DisplayName, int Status,
        int? MaxEncrypters, int UsedSeats, DateTimeOffset CreatedAtUtc, DateTimeOffset? SuspendedAtUtc);

    private sealed record KeyBody(Guid KeyId, Guid TenantId, string Label, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc);

    private sealed record CreateKeyBody(Guid KeyId, Guid TenantId, string Label, string Key, DateTimeOffset CreatedAtUtc);
}
