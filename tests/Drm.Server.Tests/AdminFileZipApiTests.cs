using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminFileZipApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-zip-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminFileZipApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Convert_zip_returns_archive_with_readme_manifest_and_share_link()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var register = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            permissions = "View, Print",
            watermarkTemplate = ""
        });
        register.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        await client.PostAsJsonAsync($"/api/admin/files/{fileId}/tags",
            new { tenantId, tag = "confidential" });

        using var response = await client.GetAsync(
            $"/api/admin/files/{fileId}/convert/zip?tenantId={tenantId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        archive.Entries.Select(e => e.Name).Should().Contain(new[] { "README.txt", "manifest.json", "share-link.txt" });

        var readmeEntry = archive.GetEntry("README.txt")!;
        using (var sr = new StreamReader(readmeEntry.Open(), Encoding.UTF8))
        {
            var readme = await sr.ReadToEndAsync();
            readme.Should().Contain("DRM Protected File");
            readme.Should().Contain(fileId.ToString());
            readme.Should().Contain("application/pdf");
        }

        var manifestEntry = archive.GetEntry("manifest.json")!;
        using (var sr = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
        {
            var manifest = await sr.ReadToEndAsync();
            manifest.Should().Contain("\"confidential\"");
            manifest.Should().Contain(fileId.ToString());
        }

        var shareEntry = archive.GetEntry("share-link.txt")!;
        using (var sr = new StreamReader(shareEntry.Open(), Encoding.UTF8))
        {
            var share = await sr.ReadToEndAsync();
            share.Should().Contain($"fileId={fileId}");
        }
    }

    [Fact]
    public async Task Convert_zip_returns_404_for_unknown_file()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/admin/files/{Guid.NewGuid()}/convert/zip?tenantId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Convert_zip_rejects_blank_tenant_id()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/admin/files/{Guid.NewGuid()}/convert/zip?tenantId={Guid.Empty}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task Zip_with_mismatched_header_returns_400_tenant_mismatch()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/admin/files/{fileId}/convert/zip?tenantId={tenantId}");
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ZipErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    private sealed record ZipErrorBody(string ReasonCode);
}
