using FluentAssertions;

namespace Drm.Server.Tests;

/// <summary>
/// Stage 11 — two web-only polish wins visible during natural demo flow:
///   A1 — Admin landing page gets an "Activity feed" panel showing the
///        latest 20 events with colored chips.
///   R1 — /share/ recipient page renders permissions as chip-style
///        badges (allowed green / denied gray-strikethrough) instead of
///        the plain bitfield string the response used to be missing.
/// Both are pure HTML/CSS/JS + one server response field; deploy via
/// docker compose, no MSI rebuild.
/// </summary>
public sealed class AdminActivityFeedAndRecipientBadgesTests
{
    private static readonly string Root = LocateRepoRoot();

    private static readonly string AdminHtml = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/admin/index.html"));
    private static readonly string AdminJs = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/admin/app.js"));
    private static readonly string AdminCss = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/admin/app.css"));
    private static readonly string ShareHtml = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/share/index.html"));
    private static readonly string ShareJs = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/share/app.js"));
    private static readonly string ShareCss = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/wwwroot/share/app.css"));
    private static readonly string ExternalShareEndpoints = File.ReadAllText(
        Path.Combine(Root, "src/Drm.Server/Endpoints/ExternalShareEndpoints.cs"));

    // ─── A1: admin activity feed ──────────────────────────────────────

    [Fact]
    public void Admin_landing_panel_has_activity_feed_container()
    {
        AdminHtml.Should().Contain(@"id=""activityFeed""",
            "admin landing must show the recent activity panel");
        AdminHtml.Should().Contain("Recent activity");
        AdminHtml.Should().Contain(@"id=""refreshActivityFeed""",
            "manual refresh button must exist");
    }

    [Fact]
    public void Admin_js_fetches_audit_endpoint_with_tenant_and_limit()
    {
        AdminJs.Should().Contain("refreshActivityFeed",
            "activity feed must have a refresh function");
        AdminJs.Should().Contain("/api/admin/audit?tenantId=",
            "feed must hit the existing audit endpoint");
        AdminJs.Should().Contain("limit=20",
            "feed must request exactly 20 events to stay scan-friendly");
    }

    [Fact]
    public void Admin_js_renders_chip_with_tone_class_per_event_type()
    {
        // The CSS class is composed as `activity-tone-${tone}` via template
        // literal, so we check for the prefix AND the three tone values
        // separately instead of the full literal.
        AdminJs.Should().Contain("activity-tone-",
            "feed must build chip classes from a tone prefix");
        AdminJs.Should().Contain("\"good\"",
            "good tone covers file_registered + viewer_opened");
        AdminJs.Should().Contain("\"danger\"",
            "danger tone covers revocations — must be red for at-a-glance scanning");
        AdminJs.Should().Contain("file_registered");
        AdminJs.Should().Contain("file_revoked");
        AdminJs.Should().Contain("external_share_viewer");
    }

    [Fact]
    public void Admin_js_formats_relative_age_for_each_row()
    {
        // "10m ago" / "2h ago" reads much faster than ISO timestamps when
        // the admin is scanning a feed.
        AdminJs.Should().Contain("formatRelativeAge");
        AdminJs.Should().Contain("ago");
    }

    [Fact]
    public void Admin_css_styles_each_chip_tone()
    {
        AdminCss.Should().Contain(".activity-tone-good");
        AdminCss.Should().Contain(".activity-tone-info");
        AdminCss.Should().Contain(".activity-tone-danger");
        AdminCss.Should().Contain(".activity-row");
    }

    [Fact]
    public void Admin_js_refreshes_activity_feed_on_load()
    {
        // Empty state has friendly copy if the admin hasn't set
        // tenant + admin key yet — refreshActivityFeed must call out
        // to the function even when credentials are unset (it will
        // handle the no-tenant case internally).
        AdminJs.Should().Contain(
            "window.addEventListener(\"load\"",
            "feed should populate as soon as the page loads");
        AdminJs.Should().Contain("refreshActivityFeed",
            "load handler must call the refresh function");
    }

    // ─── R1: recipient permission badges ──────────────────────────────

    [Fact]
    public void Share_html_has_one_chip_per_drm_capability()
    {
        ShareHtml.Should().Contain(@"id=""permissionBadges""",
            "/share/ must have a badges container");
        ShareHtml.Should().Contain(@"data-perm=""View""");
        ShareHtml.Should().Contain(@"data-perm=""Print""");
        ShareHtml.Should().Contain(@"data-perm=""Copy""");
        ShareHtml.Should().Contain(@"data-perm=""Edit""");
        ShareHtml.Should().Contain(@"data-perm=""ExportOriginal""");
    }

    [Fact]
    public void Share_js_flips_data_state_from_payload_permissions()
    {
        ShareJs.Should().Contain(@"payload.permissions",
            "JS must read the new permissions field on the viewer session response");
        ShareJs.Should().Contain(@"data-state",
            "JS must toggle each badge's data-state");
        ShareJs.Should().Contain(@"""allowed""");
        ShareJs.Should().Contain(@"""denied""");
    }

    [Fact]
    public void Share_css_styles_allowed_and_denied_states_differently()
    {
        // Visual distinction = critical for the demo moment. Lock both
        // attribute selectors so a future refactor doesn't collapse to
        // one ambiguous style.
        ShareCss.Should().Contain(@"[data-state=""allowed""]");
        ShareCss.Should().Contain(@"[data-state=""denied""]");
        ShareCss.Should().Contain("line-through",
            "denied badges use strikethrough — single-glance distinction");
    }

    [Fact]
    public void Share_css_hides_badges_until_session_verified()
    {
        // No badges should appear before the recipient passes email
        // verification — that would leak permission info to anyone with
        // the share link guess.
        ShareCss.Should().Contain(
            "preview-details[data-has-session=\"false\"] .permission-badges",
            "badges must be hidden until verification completes");
    }

    [Fact]
    public void Viewer_session_response_includes_permissions_field()
    {
        // The C# record must expose the Permissions string so the JS can
        // render badges. Lock the field name AND the population site so
        // server-side refactors don't accidentally drop the field.
        ExternalShareEndpoints.Should().Contain("string Permissions,",
            "ExternalShareViewerSessionResponse must carry the bitfield as a string");
        ExternalShareEndpoints.Should().Contain("file.Permissions.ToString()",
            "OpenViewerSessionAsync must populate Permissions from the ProtectedFile bitfield");
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }
}
