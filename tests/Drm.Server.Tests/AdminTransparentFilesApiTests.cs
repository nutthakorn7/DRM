using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminTransparentFilesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-trans-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminTransparentFilesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:TransparentTrailerSecret", "my-secret");
            });
    }

    [Fact]
    public async Task Admin_can_register_list_get_and_deregister_transparent_file()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var register = await client.PostAsJsonAsync("/api/admin/transparent-files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            originalFileName = "quarterly-report.xlsx",
            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            policyTemplateId = (Guid?)null
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<List<TransparentFileResponse>>(
            $"/api/admin/transparent-files?tenantId={tenantId}");
        list.Should().NotBeNull();
        list!.Single().FileId.Should().Be(fileId);
        list[0].OriginalFileName.Should().Be("quarterly-report.xlsx");

        var get = await client.GetFromJsonAsync<TransparentFileResponse>(
            $"/api/admin/transparent-files/{fileId}?tenantId={tenantId}");
        get!.OwnerUserId.Should().Be(ownerUserId);

        using var delete = await client.DeleteAsync(
            $"/api/admin/transparent-files/{fileId}?tenantId={tenantId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfter = await client.GetFromJsonAsync<List<TransparentFileResponse>>(
            $"/api/admin/transparent-files?tenantId={tenantId}");
        listAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_register_returns_conflict_for_duplicate_file_id()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var body = new
        {
            tenantId,
            fileId,
            ownerUserId = Guid.NewGuid(),
            originalFileName = "x.docx",
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            policyTemplateId = (Guid?)null
        };
        using var first = await client.PostAsJsonAsync("/api/admin/transparent-files", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PostAsJsonAsync("/api/admin/transparent-files", body);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_register_returns_bad_request_for_blank_identifiers()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/admin/transparent-files", new
        {
            tenantId = Guid.Empty,
            fileId = Guid.Empty,
            ownerUserId = Guid.Empty,
            originalFileName = "x.docx",
            contentType = "application/octet-stream",
            policyTemplateId = (Guid?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Trailer_secret_endpoint_returns_configured_value()
    {
        using var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SecretResponse>(
            "/api/admin/transparent-files/secret");
        response!.Secret.Should().Be("my-secret");
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record TransparentFileResponse(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string OriginalFileName,
        string ContentType,
        Guid? PolicyTemplateId,
        DateTimeOffset RegisteredAtUtc);

    private sealed record SecretResponse(string Secret);
}
