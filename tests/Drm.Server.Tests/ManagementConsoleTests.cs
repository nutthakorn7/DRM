using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class ManagementConsoleTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-management-console-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public ManagementConsoleTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_path_redirects_to_console_root()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be("/admin/");
    }

    [Fact]
    public async Task Admin_console_index_is_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/admin/");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        html.Should().Contain("DRM Management");
        html.Should().Contain("X-DRM-Admin-Key");
        html.Should().Contain("Tenant ID");
        html.Should().Contain("app.css");
        html.Should().Contain("app.js");
    }

    [Fact]
    public async Task Admin_console_assets_are_served()
    {
        using var client = factory.CreateClient();

        using var cssResponse = await client.GetAsync("/admin/app.css");
        var css = await cssResponse.Content.ReadAsStringAsync();
        using var jsResponse = await client.GetAsync("/admin/app.js");
        var js = await jsResponse.Content.ReadAsStringAsync();

        cssResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        css.Should().Contain(".workspace");
        jsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        js.Should().Contain("sessionStorage");
        js.Should().Contain("X-DRM-Admin-Key");
        js.Should().Contain("/api/admin/users");
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
