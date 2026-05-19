using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminTenantsTests : IDisposable
{
    private const string AdminApiKey = "tenants-test-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-tenants-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminTenantsTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
            });
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_returns_empty_array_on_fresh_database()
    {
        using var client = MakeAdminClient();
        using var response = await client.GetAsync("/api/admin/tenants");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenants = await response.Content.ReadFromJsonAsync<List<TenantBody>>();
        tenants.Should().BeEmpty();
    }

    [Fact]
    public async Task List_returns_created_tenant()
    {
        using var client = MakeAdminClient();
        var name = $"corp-{Guid.NewGuid():N}";
        await CreateTenant(client, name);

        using var response = await client.GetAsync("/api/admin/tenants");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenants = await response.Content.ReadFromJsonAsync<List<TenantBody>>();
        tenants.Should().Contain(t => t.Name == name);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_tenant_returns_created_with_active_status()
    {
        using var client = MakeAdminClient();
        var name = $"acme-{Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync("/api/admin/tenants", new { name, displayName = "Acme Corp" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TenantBody>();
        created!.Name.Should().Be(name);
        created.DisplayName.Should().Be("Acme Corp");
        created.Status.Should().Be(0); // Active
        created.MaxEncrypters.Should().BeNull();
        created.TenantId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_tenant_with_explicit_id_uses_that_id()
    {
        using var client = MakeAdminClient();
        var tenantId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = $"t-{Guid.NewGuid():N}", tenantId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TenantBody>();
        created!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Create_duplicate_name_returns_conflict()
    {
        using var client = MakeAdminClient();
        var name = $"dup-{Guid.NewGuid():N}";
        await CreateTenant(client, name);

        using var response = await client.PostAsJsonAsync("/api/admin/tenants", new { name });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_tenant_returns_created_tenant()
    {
        using var client = MakeAdminClient();
        var created = await CreateTenant(client, $"get-{Guid.NewGuid():N}");

        using var response = await client.GetAsync($"/api/admin/tenants/{created.TenantId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantBody>();
        body!.TenantId.Should().Be(created.TenantId);
    }

    [Fact]
    public async Task Get_unknown_tenant_returns_not_found()
    {
        using var client = MakeAdminClient();
        using var response = await client.GetAsync($"/api/admin/tenants/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Suspend_tenant_changes_status_to_suspended()
    {
        using var client = MakeAdminClient();
        var tenant = await CreateTenant(client, $"susp-{Guid.NewGuid():N}");

        using var response = await client.PatchAsJsonAsync(
            $"/api/admin/tenants/{tenant.TenantId}", new { status = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TenantBody>();
        updated!.Status.Should().Be(1); // Suspended
        updated.SuspendedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Activate_suspended_tenant_clears_suspended_at()
    {
        using var client = MakeAdminClient();
        var tenant = await CreateTenant(client, $"react-{Guid.NewGuid():N}");
        await client.PatchAsJsonAsync($"/api/admin/tenants/{tenant.TenantId}", new { status = 1 });

        using var response = await client.PatchAsJsonAsync(
            $"/api/admin/tenants/{tenant.TenantId}", new { status = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TenantBody>();
        updated!.Status.Should().Be(0); // Active
        updated.SuspendedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Update_max_encrypters_persists()
    {
        using var client = MakeAdminClient();
        var tenant = await CreateTenant(client, $"seats-{Guid.NewGuid():N}");

        using var response = await client.PatchAsJsonAsync(
            $"/api/admin/tenants/{tenant.TenantId}", new { maxEncrypters = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TenantBody>();
        updated!.MaxEncrypters.Should().Be(5);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient MakeAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        return client;
    }

    private async Task<TenantBody> CreateTenant(HttpClient client, string name, int? maxEncrypters = null)
    {
        var body = maxEncrypters.HasValue
            ? (object)new { name, maxEncrypters }
            : new { name };
        using var response = await client.PostAsJsonAsync("/api/admin/tenants", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantBody>())!;
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
        Guid TenantId,
        string Name,
        string DisplayName,
        int Status,
        int? MaxEncrypters,
        int UsedSeats,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? SuspendedAtUtc);
}
