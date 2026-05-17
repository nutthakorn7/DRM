using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminSecureContainersApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-cont-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminSecureContainersApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Register_then_get_then_list_then_delete_secure_container()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var register = await client.PostAsJsonAsync("/api/admin/secure-containers", new
        {
            tenantId,
            containerId,
            ownerUserId,
            displayName = "Project Atlas",
            policyTemplateId = (Guid?)null,
            files = new[]
            {
                new { relativePath = "design/cover.ai", size = 12345L },
                new { relativePath = "design/logo.psd", size = 6789L },
                new { relativePath = "specs.docx", size = 4096L }
            }
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var fetched = await client.GetFromJsonAsync<ContainerResponse>(
            $"/api/admin/secure-containers/{containerId}?tenantId={tenantId}");
        fetched!.DisplayName.Should().Be("Project Atlas");
        fetched.FileCount.Should().Be(3);
        fetched.TotalBytes.Should().Be(12345 + 6789 + 4096);
        fetched.Files.Should().HaveCount(3);
        fetched.Files.Select(f => f.RelativePath)
            .Should().BeEquivalentTo(new[] { "design/cover.ai", "design/logo.psd", "specs.docx" });

        var summaries = await client.GetFromJsonAsync<List<ContainerSummary>>(
            $"/api/admin/secure-containers?tenantId={tenantId}");
        summaries.Should().ContainSingle();

        using var delete = await client.DeleteAsync(
            $"/api/admin/secure-containers/{containerId}?tenantId={tenantId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var refetch = await client.GetAsync(
            $"/api/admin/secure-containers/{containerId}?tenantId={tenantId}");
        refetch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Register_returns_bad_request_for_empty_files()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/admin/secure-containers", new
        {
            tenantId = Guid.NewGuid(),
            containerId = Guid.NewGuid(),
            ownerUserId = Guid.NewGuid(),
            displayName = "Empty container",
            policyTemplateId = (Guid?)null,
            files = Array.Empty<object>()
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_returns_conflict_on_duplicate_container_id()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var body = new
        {
            tenantId,
            containerId,
            ownerUserId = Guid.NewGuid(),
            displayName = "Dupe",
            policyTemplateId = (Guid?)null,
            files = new[] { new { relativePath = "x.txt", size = 1L } }
        };
        using var first = await client.PostAsJsonAsync("/api/admin/secure-containers", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PostAsJsonAsync("/api/admin/secure-containers", body);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record ContainerSummary(
        Guid TenantId, Guid ContainerId, Guid OwnerUserId, string DisplayName,
        int FileCount, long TotalBytes, Guid? PolicyTemplateId, DateTimeOffset CreatedAtUtc);

    private sealed record ContainerResponse(
        Guid TenantId, Guid ContainerId, Guid OwnerUserId, string DisplayName,
        int FileCount, long TotalBytes, Guid? PolicyTemplateId, DateTimeOffset CreatedAtUtc,
        IReadOnlyList<ContainerFile> Files);

    private sealed record ContainerFile(int OrdinalIndex, string RelativePath, long Size);

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task Register_secure_container_with_mismatched_header_returns_400()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/secure-containers")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(),
                containerId = Guid.NewGuid(),
                ownerUserId = Guid.NewGuid(),
                displayName = "Q4 financials",
                files = new[] { new { ordinalIndex = 0, relativePath = "a.pdf", size = 100L } },
            })
        };
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    private sealed record ErrorBody(string ReasonCode);
}
