using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Tests;

public sealed class AdminIdentityApiTests : IDisposable
{
    private const string AdminApiKey = "shared-admin-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-admin-identity-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminIdentityApiTests()
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
    public async Task Whoami_via_shared_key_returns_default_super_admin()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        using var response = await client.GetAsync("/api/admin/identity/whoami");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<WhoamiBody>();
        body!.AdminUserId.Should().Be(AdminSystemRoles.DefaultSuperAdminUserId);
        body.RoleId.Should().Be(AdminSystemRoles.SuperAdminId);
        body.SharedKeyFallback.Should().BeTrue();
        body.Permissions.Should().Contain(AdminPermissions.Wildcard);
    }

    [Fact]
    public async Task Whoami_without_credential_returns_401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/admin/identity/whoami");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Invalid_token_returns_403()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.TokenHeaderName, "drm_admin_not-a-real-token");

        using var response = await client.GetAsync("/api/admin/identity/whoami");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_roles_returns_four_system_roles()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        using var response = await client.GetAsync("/api/admin/identity/roles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await response.Content.ReadFromJsonAsync<List<RoleBody>>();
        roles.Should().NotBeNull();
        roles!.Should().Contain(r => r.Id == AdminSystemRoles.SuperAdminId  && r.IsSystem);
        roles.Should().Contain(r => r.Id == AdminSystemRoles.TenantAdminId && r.IsSystem);
        roles.Should().Contain(r => r.Id == AdminSystemRoles.AuditorId     && r.IsSystem);
        roles.Should().Contain(r => r.Id == AdminSystemRoles.ReadOnlyId    && r.IsSystem);
    }

    [Fact]
    public async Task Create_admin_returns_one_time_token_and_token_authenticates()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        var createBody = new
        {
            email = $"alice-{Guid.NewGuid():N}@example.com",
            displayName = "Alice Admin",
            roleId = AdminSystemRoles.TenantAdminId,
            tokenLabel = "test"
        };
        using var createResponse = await client.PostAsJsonAsync("/api/admin/identity/admins", createBody);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateAdminBody>();
        created!.Token.Should().StartWith(AdminTokenCrypto.TokenPrefix);
        created.AdminUserId.Should().NotBe(Guid.Empty);

        // Use the new token to call whoami
        using var tokenClient = factory.CreateClient();
        tokenClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.TokenHeaderName, created.Token);
        using var whoami = await tokenClient.GetAsync("/api/admin/identity/whoami");
        whoami.StatusCode.Should().Be(HttpStatusCode.OK);
        var whoamiBody = await whoami.Content.ReadFromJsonAsync<WhoamiBody>();
        whoamiBody!.AdminUserId.Should().Be(created.AdminUserId);
        whoamiBody.SharedKeyFallback.Should().BeFalse();
        whoamiBody.Permissions.Should().NotContain(AdminPermissions.Wildcard);
        whoamiBody.Permissions.Should().Contain(AdminPermissions.TenantsRead);
    }

    [Fact]
    public async Task Cannot_disable_default_super_admin()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        using var response = await client.PostAsync(
            $"/api/admin/identity/admins/{AdminSystemRoles.DefaultSuperAdminUserId}/disable",
            content: null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoked_token_no_longer_authenticates()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        var createBody = new
        {
            email = $"bob-{Guid.NewGuid():N}@example.com",
            displayName = "Bob",
            roleId = AdminSystemRoles.ReadOnlyId,
            tokenLabel = "test"
        };
        using var createResponse = await client.PostAsJsonAsync("/api/admin/identity/admins", createBody);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateAdminBody>();

        using var revokeResponse = await client.PostAsync(
            $"/api/admin/identity/tokens/{created!.TokenId}/revoke", content: null);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var tokenClient = factory.CreateClient();
        tokenClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.TokenHeaderName, created.Token);
        using var whoami = await tokenClient.GetAsync("/api/admin/identity/whoami");
        whoami.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Readonly_role_cannot_create_admins()
    {
        using var bootstrap = factory.CreateClient();
        bootstrap.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        var createBody = new
        {
            email = $"carol-{Guid.NewGuid():N}@example.com",
            displayName = "Carol",
            roleId = AdminSystemRoles.ReadOnlyId,
            tokenLabel = "test"
        };
        using var createResponse = await bootstrap.PostAsJsonAsync("/api/admin/identity/admins", createBody);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateAdminBody>();

        using var readonlyClient = factory.CreateClient();
        readonlyClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.TokenHeaderName, created!.Token);

        var attempt = new
        {
            email = "should-fail@example.com",
            displayName = "Should Fail",
            roleId = AdminSystemRoles.AuditorId
        };
        using var response = await readonlyClient.PostAsJsonAsync("/api/admin/identity/admins", attempt);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Bootstrap_seeds_default_super_admin_row_in_database()
    {
        using var client = factory.CreateClient();
        // Triggers the app initialization which runs seed.
        _ = await client.GetAsync("/healthz");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var defaultAdmin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Id == AdminSystemRoles.DefaultSuperAdminUserId);
        defaultAdmin.Should().NotBeNull();
        defaultAdmin!.RoleId.Should().Be(AdminSystemRoles.SuperAdminId);

        var roleCount = await db.AdminRoles.CountAsync();
        roleCount.Should().BeGreaterThanOrEqualTo(4);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record WhoamiBody(Guid AdminUserId, Guid RoleId, string DisplayName, string Email, List<string> Permissions, bool SharedKeyFallback);
    private sealed record RoleBody(Guid Id, string Name, List<string> Permissions, bool IsSystem);
    private sealed record CreateAdminBody(Guid AdminUserId, string Email, string DisplayName, Guid RoleId, string RoleName, Guid TokenId, string Token, DateTimeOffset? TokenExpiresAtUtc);
}
