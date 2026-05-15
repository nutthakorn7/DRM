using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class ClientApiKeyAuthenticationTests : IDisposable
{
    private const string AdminApiKey = "secret-admin-key";
    private const string ClientApiKey = "secret-client-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-client-auth-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public ClientApiKeyAuthenticationTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Security:ClientApiKey", ClientApiKey);
            });
    }

    [Fact]
    public async Task Client_endpoint_requires_api_key_when_configured()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/audit?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Client_endpoint_rejects_wrong_api_key_when_configured()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Client-Key", "wrong-key");

        using var response = await client.GetAsync($"/api/audit?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Client_endpoint_allows_matching_api_key_when_configured()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Client-Key", ClientApiKey);

        using var response = await client.GetAsync($"/api/audit?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_endpoint_still_uses_admin_key_only()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-DRM-Admin-Key", AdminApiKey);

        using var response = await client.GetAsync($"/api/admin/users?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_endpoint_does_not_require_client_api_key()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
}
