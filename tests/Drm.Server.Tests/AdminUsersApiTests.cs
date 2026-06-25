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

    [Fact]
    public async Task Create_user_without_user_id_generates_one()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var create = await client.PostAsJsonAsync("/api/admin/users", new
        {
            tenantId, email = "noid@example.com", displayName = "No Id"
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<UserResponse>();
        created!.UserId.Should().NotBe(Guid.Empty, "the server generates a user id when the admin omits one");
    }

    [Fact]
    public async Task Two_users_created_without_user_id_do_not_collide()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var a = await client.PostAsJsonAsync("/api/admin/users", new { tenantId, email = "a@example.com", displayName = "A" });
        using var b = await client.PostAsJsonAsync("/api/admin/users", new { tenantId, email = "b@example.com", displayName = "B" });
        a.StatusCode.Should().Be(HttpStatusCode.Created);
        b.StatusCode.Should().Be(HttpStatusCode.Created, "a second id-less user must not collide on Guid.Empty");

        var users = await client.GetFromJsonAsync<List<UserResponse>>($"/api/admin/users?tenantId={tenantId}");
        users!.Should().HaveCount(2);
        users.Select(u => u.UserId).Should().OnlyHaveUniqueItems().And.NotContain(Guid.Empty);
    }

    [Fact]
    public async Task Admin_create_user_returns_conflict_for_duplicate_user_id_in_same_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var firstCreate = await CreateUserAsync(client, tenantId, userId, "owner@example.com");
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var duplicateCreate = await CreateUserAsync(client, tenantId, userId, "other@example.com");

        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_create_user_returns_conflict_for_duplicate_email_in_same_tenant()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var firstCreate = await CreateUserAsync(client, tenantId, Guid.NewGuid(), "owner@example.com");
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        using var duplicateCreate = await CreateUserAsync(client, tenantId, Guid.NewGuid(), "owner@example.com");

        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_can_create_same_email_in_different_tenants()
    {
        using var client = factory.CreateClient();
        var email = "owner@example.com";

        using var firstCreate = await CreateUserAsync(client, Guid.NewGuid(), Guid.NewGuid(), email);
        using var secondCreate = await CreateUserAsync(client, Guid.NewGuid(), Guid.NewGuid(), email);

        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        secondCreate.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private static Task<HttpResponseMessage> CreateUserAsync(
        HttpClient client,
        Guid tenantId,
        Guid userId,
        string email)
    {
        return client.PostAsJsonAsync("/api/admin/users", new
        {
            tenantId,
            userId,
            email,
            displayName = "Owner User"
        });
    }

    private sealed record UserResponse(Guid UserId, Guid TenantId, string Email, string DisplayName);

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task Create_user_with_mismatched_tenant_header_returns_400_tenant_mismatch()
    {
        using var client = factory.CreateClient();
        var bodyTenant = Guid.NewGuid();
        var headerTenant = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = JsonContent.Create(new
            {
                tenantId = bodyTenant,
                userId = Guid.NewGuid(),
                email = "drift@example.com",
                displayName = "Drift",
            })
        };
        request.Headers.Add("X-DRM-Tenant-Id", headerTenant.ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    private sealed record ErrorBody(string ReasonCode);
}
