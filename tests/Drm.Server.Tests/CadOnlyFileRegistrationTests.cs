using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class CadOnlyFileRegistrationTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-cad-only-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public CadOnlyFileRegistrationTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Files:CadOnly:Enabled", "true");
            });
    }

    [Fact]
    public async Task CadOnly_mode_rejects_non_cad_content_types()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/files", NewRegisterRequest("application/pdf", "drawing.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("unsupported_file_type");
    }

    [Fact]
    public async Task CadOnly_mode_accepts_cad_content_types()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/files", NewRegisterRequest("image/vnd.dwg", "model.dwg"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CadOnly_mode_rejects_cad_content_type_without_cad_filename()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/files", NewRegisterRequest("image/vnd.dwg", "renamed.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("unsupported_file_type");
    }

    public void Dispose()
    {
        factory.Dispose();
        DeleteDatabaseFiles(databasePath);
    }

    private static object NewRegisterRequest(string contentType, string originalFileName)
        => new
        {
            tenantId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            ownerUserId = Guid.NewGuid(),
            contentType,
            originalFileName,
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
            permissions = "View"
        };

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
