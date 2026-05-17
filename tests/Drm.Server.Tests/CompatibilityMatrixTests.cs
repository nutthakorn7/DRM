using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class CompatibilityMatrixTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-compat-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public CompatibilityMatrixTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Matrix_endpoint_returns_all_categories_and_known_issues()
    {
        using var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<MatrixResponse>("/api/admin/compatibility-matrix");
        response.Should().NotBeNull();
        response!.Categories.Select(c => c.Name)
            .Should().Contain(new[] { "Documents", "Design / Image", "CAD", "Video", "Simulation", "Known issues" });

        var cad = response.Categories.Single(c => c.Name == "CAD");
        cad.Entries.Should().Contain(e => e.Application == "Autodesk AutoCAD");
        cad.Entries.Should().Contain(e => e.Application == "Dassault SolidWorks");
        cad.Entries.Should().Contain(e => e.Application == "Siemens Solid Edge");
        cad.Entries.Should().Contain(e => e.Application.Contains("XVL"));

        response.KnownIssueCount.Should().Be(3);
        response.GeneratedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Known_issues_have_required_notes()
    {
        foreach (var entry in CompatibilityMatrix.KnownIssues)
        {
            entry.Notes.Should().NotBeNullOrWhiteSpace($"known issue '{entry.Application}' must have notes");
        }
    }

    [Fact]
    public void IsBlocked_returns_false_for_any_content_type_in_v1_matrix()
    {
        // V1 matrix surfaces guidance only; no content type is hard-blocked.
        // This test pins that contract so future hard-blocks land deliberately.
        CompatibilityMatrix.IsBlocked("application/pdf", out _).Should().BeFalse();
        CompatibilityMatrix.IsBlocked("application/octet-stream", out _).Should().BeFalse();
        CompatibilityMatrix.IsBlocked("", out _).Should().BeFalse();
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record MatrixResponse(
        IReadOnlyList<CompatibilityCategory> Categories,
        int KnownIssueCount,
        DateTimeOffset GeneratedAtUtc);

    private sealed record CompatibilityCategory(string Name, IReadOnlyList<CompatibilityEntry> Entries);

    private sealed record CompatibilityEntry(string Application, string Versions, string Status, string? Notes);
}
