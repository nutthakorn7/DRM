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

    private sealed record UserResponse(Guid UserId, Guid TenantId, string Email, string DisplayName);
}
