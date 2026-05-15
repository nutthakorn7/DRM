using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class ExternalShareViewerShellTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-share-viewer-shell-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public ExternalShareViewerShellTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Root_path_redirects_to_share_viewer()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be("/share/");
    }

    [Fact]
    public async Task Share_path_redirects_to_viewer_root()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/share");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().Be("/share/");
    }

    [Fact]
    public async Task Share_viewer_index_is_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/share/");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        html.Should().Contain("External Share Viewer");
        html.Should().Contain("verificationStartForm");
        html.Should().Contain("verificationConfirmForm");
        html.Should().Contain("viewerStatus");
        html.Should().Contain("Download disabled");
        html.Should().Contain("Print disabled");
        html.Should().Contain("Export disabled");
        html.Should().Contain("/share/app.css");
        html.Should().Contain("/share/app.js");
    }

    [Fact]
    public async Task Share_viewer_assets_are_served()
    {
        using var client = factory.CreateClient();

        using var cssResponse = await client.GetAsync("/share/app.css");
        var css = await cssResponse.Content.ReadAsStringAsync();
        using var jsResponse = await client.GetAsync("/share/app.js");
        var js = await jsResponse.Content.ReadAsStringAsync();

        cssResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        css.Should().Contain(".viewer-shell");
        css.Should().Contain(".locked-preview");
        jsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        js.Should().Contain("/api/share-links/verification/start");
        js.Should().Contain("/api/share-links/verification/confirm");
        js.Should().Contain("/api/share-links/viewer/session");
        js.Should().NotContain("localStorage");
        js.Should().NotContain("sessionStorage");
        js.Should().NotContain("wrappedKey");
        js.Should().NotContain("ciphertext");
        js.Should().NotContain("decrypted");
        js.Should().NotContain("/unwrap");
        js.Should().NotContain("/download");
        js.Should().NotContain("print()");
        js.Should().NotContain("exportOriginal");
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
