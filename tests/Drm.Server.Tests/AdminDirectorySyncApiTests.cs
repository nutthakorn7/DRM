using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminDirectorySyncApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-dir-sync-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminDirectorySyncApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_save_and_retrieve_directory_config()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var put = await client.PutAsJsonAsync("/api/admin/directory/config", new
        {
            tenantId,
            entraTenantId = "contoso.onmicrosoft.com",
            clientId = "11111111-1111-1111-1111-111111111111",
            clientSecret = "supersecret"
        });

        put.StatusCode.Should().Be(HttpStatusCode.Created);

        var config = await client.GetFromJsonAsync<DirectorySyncConfigResponse>(
            $"/api/admin/directory/config?tenantId={tenantId}");

        config.Should().NotBeNull();
        config!.EntraTenantId.Should().Be("contoso.onmicrosoft.com");
        config.ClientId.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task Get_directory_config_returns_not_found_when_unconfigured()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/admin/directory/config?tenantId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_update_existing_directory_config()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        using var first = await client.PutAsJsonAsync("/api/admin/directory/config", new
        {
            tenantId,
            entraTenantId = "old.onmicrosoft.com",
            clientId = "old-client-id",
            clientSecret = "secret1"
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PutAsJsonAsync("/api/admin/directory/config", new
        {
            tenantId,
            entraTenantId = "new.onmicrosoft.com",
            clientId = "new-client-id",
            clientSecret = "secret2"
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var config = await client.GetFromJsonAsync<DirectorySyncConfigResponse>(
            $"/api/admin/directory/config?tenantId={tenantId}");

        config!.EntraTenantId.Should().Be("new.onmicrosoft.com");
        config.ClientId.Should().Be("new-client-id");
    }

    [Fact]
    public async Task Trigger_sync_returns_not_found_when_unconfigured()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/admin/directory/sync",
            new { tenantId = Guid.NewGuid() });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_config_does_not_return_client_secret()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();

        await client.PutAsJsonAsync("/api/admin/directory/config", new
        {
            tenantId,
            entraTenantId = "contoso.onmicrosoft.com",
            clientId = "some-client-id",
            clientSecret = "very-secret-value"
        });

        var raw = await client.GetStringAsync($"/api/admin/directory/config?tenantId={tenantId}");
        raw.Should().NotContain("very-secret-value");
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var f in new[] { path, $"{path}-wal", $"{path}-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private sealed record DirectorySyncConfigResponse(
        Guid TenantId,
        string EntraTenantId,
        string ClientId,
        DateTimeOffset? LastSyncAtUtc,
        string? LastSyncStatus,
        int? LastSyncUserCount,
        int? LastSyncGroupCount);
}
