using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminTenantScopeTests : IDisposable
{
    private const string AdminApiKey = "tenant-scope-test-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-tenant-scope-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminTenantScopeTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
            });
    }

    // ── Create admin with scope ──────────────────────────────────────────────

    [Fact]
    public async Task Create_admin_with_tenant_scope_returns_scope_in_response()
    {
        var tenantId = Guid.NewGuid();
        using var client = MakeSharedKeyClient();

        var body = new
        {
            email = $"scoped-{Guid.NewGuid():N}@example.com",
            displayName = "Scoped Admin",
            roleId = AdminSystemRoles.TenantAdminId,
            tokenLabel = "test",
            tenantScope = tenantId
        };

        using var response = await client.PostAsJsonAsync("/api/admin/identity/admins", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<CreateAdminBody>();
        created!.TenantScope.Should().Be(tenantId);
    }

    [Fact]
    public async Task Create_admin_without_scope_returns_null_scope()
    {
        using var client = MakeSharedKeyClient();

        var body = new
        {
            email = $"global-{Guid.NewGuid():N}@example.com",
            displayName = "Global Admin",
            roleId = AdminSystemRoles.TenantAdminId,
            tokenLabel = "test"
        };

        using var response = await client.PostAsJsonAsync("/api/admin/identity/admins", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<CreateAdminBody>();
        created!.TenantScope.Should().BeNull();
    }

    // ── WhoAmI reflects scope ────────────────────────────────────────────────

    [Fact]
    public async Task Whoami_for_scoped_admin_returns_tenant_scope()
    {
        var tenantId = Guid.NewGuid();
        var token = await CreateScopedAdmin(tenantId);

        using var tokenClient = MakeTokenClient(token);
        using var response = await tokenClient.GetAsync("/api/admin/identity/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var whoami = await response.Content.ReadFromJsonAsync<WhoamiBody>();
        whoami!.TenantScope.Should().Be(tenantId);
    }

    [Fact]
    public async Task Whoami_for_global_admin_returns_null_scope()
    {
        var token = await CreateGlobalAdmin();

        using var tokenClient = MakeTokenClient(token);
        using var response = await tokenClient.GetAsync("/api/admin/identity/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var whoami = await response.Content.ReadFromJsonAsync<WhoamiBody>();
        whoami!.TenantScope.Should().BeNull();
    }

    // ── Tenant-scope enforcement on user endpoints ───────────────────────────

    [Fact]
    public async Task Scoped_admin_can_list_users_for_own_tenant()
    {
        var tenantId = Guid.NewGuid();
        var token = await CreateScopedAdmin(tenantId);

        using var tokenClient = MakeTokenClient(token);
        tokenClient.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        using var response = await tokenClient.GetAsync($"/api/admin/users?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Scoped_admin_denied_listing_users_for_other_tenant()
    {
        var ownTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var token = await CreateScopedAdmin(ownTenant);

        using var tokenClient = MakeTokenClient(token);
        tokenClient.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", otherTenant.ToString());
        using var response = await tokenClient.GetAsync($"/api/admin/users?tenantId={otherTenant}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_scope_denied");
    }

    [Fact]
    public async Task Global_admin_can_list_users_for_any_tenant()
    {
        var tenantId = Guid.NewGuid();
        var token = await CreateGlobalAdmin();

        using var tokenClient = MakeTokenClient(token);
        tokenClient.DefaultRequestHeaders.Add("X-DRM-Tenant-Id", tenantId.ToString());
        using var response = await tokenClient.GetAsync($"/api/admin/users?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Admin list shows scope ───────────────────────────────────────────────

    [Fact]
    public async Task Admin_list_includes_tenant_scope_field()
    {
        var tenantId = Guid.NewGuid();
        await CreateScopedAdmin(tenantId);

        using var client = MakeSharedKeyClient();
        using var response = await client.GetAsync("/api/admin/identity/admins");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var admins = await response.Content.ReadFromJsonAsync<List<AdminListBody>>();
        admins.Should().Contain(a => a.TenantScope == tenantId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient MakeSharedKeyClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        return client;
    }

    private HttpClient MakeTokenClient(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.TokenHeaderName, token);
        return client;
    }

    private async Task<string> CreateScopedAdmin(Guid tenantId)
    {
        using var client = MakeSharedKeyClient();
        var body = new
        {
            email = $"scoped-{Guid.NewGuid():N}@example.com",
            displayName = "Scoped Admin",
            roleId = AdminSystemRoles.TenantAdminId,
            tokenLabel = "test",
            tenantScope = tenantId
        };
        using var response = await client.PostAsJsonAsync("/api/admin/identity/admins", body);
        var created = await response.Content.ReadFromJsonAsync<CreateAdminBody>();
        return created!.Token;
    }

    private async Task<string> CreateGlobalAdmin()
    {
        using var client = MakeSharedKeyClient();
        var body = new
        {
            email = $"global-{Guid.NewGuid():N}@example.com",
            displayName = "Global Admin",
            roleId = AdminSystemRoles.TenantAdminId,
            tokenLabel = "test"
        };
        using var response = await client.PostAsJsonAsync("/api/admin/identity/admins", body);
        var created = await response.Content.ReadFromJsonAsync<CreateAdminBody>();
        return created!.Token;
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record WhoamiBody(Guid AdminUserId, Guid RoleId, string DisplayName, string Email, List<string> Permissions, bool SharedKeyFallback, Guid? TenantScope = null);
    private sealed record CreateAdminBody(Guid AdminUserId, string Email, string DisplayName, Guid RoleId, string RoleName, Guid TokenId, string Token, DateTimeOffset? TokenExpiresAtUtc, Guid? TenantScope = null);
    private sealed record AdminListBody(Guid Id, string Email, Guid RoleId, string RoleName, bool Disabled, int ActiveTokenCount, Guid? TenantScope = null);
    private sealed record ErrorBody(string ReasonCode);
}
