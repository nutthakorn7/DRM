using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminLicenseApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-license-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminLicenseApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:License:EnabledTiers", "Standard, Api");
                builder.UseSetting("Drm:License:PaidEncrypterCount", "10");
            });
    }

    [Fact]
    public async Task License_returns_configured_tiers_and_viewer_multiplier()
    {
        using var client = factory.CreateClient();
        var license = await client.GetFromJsonAsync<LicenseResponse>("/api/admin/license");
        license.Should().NotBeNull();
        license!.EnabledTiers.Should().BeEquivalentTo(new[] { "Standard", "Api" });
        license.PaidEncrypterCount.Should().Be(10);
        license.FreeViewerCount.Should().Be(90);
    }

    [Fact]
    public void License_tier_parser_returns_all_when_value_blank()
    {
        LicenseTierParser.ParseConfigured(null).Should().Be(LicenseTier.All);
        LicenseTierParser.ParseConfigured("").Should().Be(LicenseTier.All);
        LicenseTierParser.ParseConfigured("All").Should().Be(LicenseTier.All);
    }

    [Fact]
    public void License_tier_parser_parses_csv_tokens()
    {
        var tier = LicenseTierParser.ParseConfigured("Standard, Box, Outlook");
        tier.HasFlag(LicenseTier.Standard).Should().BeTrue();
        tier.HasFlag(LicenseTier.Box).Should().BeTrue();
        tier.HasFlag(LicenseTier.Outlook).Should().BeTrue();
        tier.HasFlag(LicenseTier.Api).Should().BeFalse();
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed record LicenseResponse(
        IReadOnlyList<string> EnabledTiers,
        int PaidEncrypterCount,
        int FreeViewerCount);
}
