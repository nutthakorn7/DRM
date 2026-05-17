using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

/// <summary>
/// Verifies the Phase 5AS-polish Word add-in ships its manifest and
/// taskpane through the server's static file middleware, and that the
/// manifest references the resources the Office sideload flow expects.
/// </summary>
public sealed class WordAddInAssetsTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-word-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public WordAddInAssetsTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                b.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Word_addin_manifest_is_served_and_is_well_formed_office_app_xml()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/word-addin/manifest.xml");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(body);
        var root = doc.Root;
        root.Should().NotBeNull();
        root!.Name.LocalName.Should().Be("OfficeApp");
        var ns = root.Name.Namespace;

        var displayName = root.Element(ns + "DisplayName")?.Attribute("DefaultValue")?.Value;
        displayName.Should().Be("DRM Protect");

        var host = root.Element(ns + "Hosts")?.Element(ns + "Host")?.Attribute("Name")?.Value;
        host.Should().Be("Document");

        // Permissions must allow reading + writing the document so the
        // taskpane can call getFileAsync.
        root.Element(ns + "Permissions")?.Value.Should().Be("ReadWriteDocument");

        // The manifest references taskpane.html and three icon sizes; the
        // sideload flow refuses any manifest missing these resources.
        body.Should().Contain("/word-addin/taskpane.html");
        body.Should().Contain("icon-16.png");
        body.Should().Contain("icon-32.png");
        body.Should().Contain("icon-80.png");
    }

    [Fact]
    public async Task Word_addin_taskpane_html_is_served()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/word-addin/taskpane.html");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Office.context.document.getFileAsync");
        body.Should().Contain("/api/me/share");
        body.Should().Contain("Protect and send");
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }
}
