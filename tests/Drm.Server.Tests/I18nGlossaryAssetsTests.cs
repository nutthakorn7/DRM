using System.Text.Json;
using FluentAssertions;

namespace Drm.Server.Tests;

/// <summary>
/// Validates the shipped glossary i18n assets: every locale must parse as
/// JSON, must use a subset of the EN keys (no orphan keys that the UI
/// would never look up), and the EN file must remain the source of truth.
/// </summary>
public sealed class I18nGlossaryAssetsTests
{
    private static readonly string AdminDir = Path.Combine(
        LocateRepoRoot(), "src/Drm.Server/wwwroot/admin");

    [Fact]
    public void English_glossary_loads_and_has_minimum_term_count()
    {
        var path = Path.Combine(AdminDir, "glossary.en.json");
        File.Exists(path).Should().BeTrue("the EN glossary is the source of truth");

        var map = LoadJson(path);
        map.Should().NotBeNull();
        map!.Count.Should().BeGreaterThanOrEqualTo(20,
            "the glossary covers at least 20 jargon terms so non-technical users have help everywhere they need it");
    }

    [Theory]
    [InlineData("th")]
    [InlineData("ja")]
    public void Locale_glossary_uses_only_keys_present_in_english(string locale)
    {
        var enMap = LoadJson(Path.Combine(AdminDir, "glossary.en.json"));
        var localePath = Path.Combine(AdminDir, $"glossary.{locale}.json");
        File.Exists(localePath).Should().BeTrue($"glossary.{locale}.json must ship");

        var localeMap = LoadJson(localePath);
        localeMap.Should().NotBeNull();

        var orphans = localeMap!.Keys.Where(k => !enMap!.ContainsKey(k)).ToList();
        orphans.Should().BeEmpty(
            $"locale '{locale}' must not declare terms missing from EN (orphan terms are never decorated): {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Original_glossary_json_remains_for_backwards_compat()
    {
        // The Phase 5AS release shipped a single glossary.json that the JS
        // still falls back to. Keep it in lockstep with the EN map so
        // clients that haven't picked up the locale-aware loader still get
        // the canonical EN tooltips.
        var legacy = Path.Combine(AdminDir, "glossary.json");
        File.Exists(legacy).Should().BeTrue("legacy glossary.json must still exist for backwards-compat");

        var enMap = LoadJson(Path.Combine(AdminDir, "glossary.en.json"));
        var legacyMap = LoadJson(legacy);
        legacyMap.Should().BeEquivalentTo(enMap!);
    }

    private static Dictionary<string, string>? LoadJson(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(bytes);
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
