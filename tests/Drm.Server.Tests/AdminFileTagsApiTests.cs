using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminFileTagsApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-tags-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminFileTagsApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Admin_can_add_list_and_remove_tags_per_file()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var add1 = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/tags", new { tenantId, tag = "confidential" });
        add1.StatusCode.Should().Be(HttpStatusCode.Created);

        using var add2 = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/tags", new { tenantId, tag = "q4-2026" });
        add2.StatusCode.Should().Be(HttpStatusCode.Created);

        var tags = await client.GetFromJsonAsync<List<string>>(
            $"/api/admin/files/{fileId}/tags?tenantId={tenantId}");
        tags.Should().BeEquivalentTo(new[] { "confidential", "q4-2026" });

        using var remove = await client.DeleteAsync(
            $"/api/admin/files/{fileId}/tags/confidential?tenantId={tenantId}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tagsAfter = await client.GetFromJsonAsync<List<string>>(
            $"/api/admin/files/{fileId}/tags?tenantId={tenantId}");
        tagsAfter.Should().BeEquivalentTo(new[] { "q4-2026" });
    }

    [Fact]
    public async Task Admin_can_list_all_tags_with_file_counts()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var file1 = Guid.NewGuid();
        var file2 = Guid.NewGuid();
        var file3 = Guid.NewGuid();

        await client.PostAsJsonAsync($"/api/admin/files/{file1}/tags", new { tenantId, tag = "hr" });
        await client.PostAsJsonAsync($"/api/admin/files/{file2}/tags", new { tenantId, tag = "hr" });
        await client.PostAsJsonAsync($"/api/admin/files/{file3}/tags", new { tenantId, tag = "finance" });

        var summaries = await client.GetFromJsonAsync<List<TagSummary>>(
            $"/api/admin/tags?tenantId={tenantId}");
        summaries.Should().HaveCount(2);
        summaries!.Single(s => s.Tag == "hr").FileCount.Should().Be(2);
        summaries.Single(s => s.Tag == "finance").FileCount.Should().Be(1);
    }

    [Fact]
    public async Task Admin_can_list_files_by_tag_excluding_other_tenants()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var fileMine = Guid.NewGuid();
        var fileOther = Guid.NewGuid();

        await client.PostAsJsonAsync($"/api/admin/files/{fileMine}/tags", new { tenantId, tag = "secret" });
        await client.PostAsJsonAsync($"/api/admin/files/{fileOther}/tags", new { tenantId = otherTenant, tag = "secret" });

        var ids = await client.GetFromJsonAsync<List<Guid>>(
            $"/api/admin/files-by-tag?tenantId={tenantId}&tag=secret");
        ids.Should().Equal(fileMine);
    }

    [Fact]
    public async Task Admin_add_duplicate_tag_returns_conflict()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var first = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/tags", new { tenantId, tag = "alpha" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/tags", new { tenantId, tag = "alpha" });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_remove_missing_tag_returns_not_found()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        using var remove = await client.DeleteAsync(
            $"/api/admin/files/{fileId}/tags/ghost?tenantId={tenantId}");
        remove.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record TagSummary(string Tag, int FileCount);
}
