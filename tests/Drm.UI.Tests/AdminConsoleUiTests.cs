using FluentAssertions;
using Microsoft.Playwright;

namespace Drm.UI.Tests;

[Collection(nameof(DrmUiTestCollection))]
public sealed class AdminConsoleUiTests
{
    private readonly DrmServerFixture server;
    private readonly PlaywrightFixture playwright;

    public AdminConsoleUiTests(DrmServerFixture server, PlaywrightFixture playwright)
    {
        this.server = server;
        this.playwright = playwright;
    }

    [Fact]
    public async Task Welcome_screen_shows_on_first_visit_and_dismisses_after_one_click()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{server.BaseUrl}/admin/");

        // Welcome screen visible on a fresh localStorage state.
        var welcome = page.Locator("#welcomeScreen");
        await welcome.WaitForAsync();
        var hiddenAttr = await welcome.GetAttributeAsync("hidden");
        hiddenAttr.Should().BeNull("welcome screen should be visible on first visit");

        var createButton = page.Locator("#welcomeStart");
        await createButton.ClickAsync();

        // After dismissal, tenant + admin user fields should be populated and
        // the welcome screen should hide.
        await page.WaitForFunctionAsync("() => document.getElementById('welcomeScreen')?.hidden === true");
        var tenantValue = await page.Locator("#tenantId").InputValueAsync();
        var userValue = await page.Locator("#adminUserId").InputValueAsync();
        tenantValue.Should().NotBeNullOrWhiteSpace();
        userValue.Should().NotBeNullOrWhiteSpace();
        tenantValue.Should().HaveLength(36, "tenant ID should be a generated UUID");
    }

    [Fact]
    public async Task Tabs_switch_active_panel_and_subnav_rebuilds()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{server.BaseUrl}/admin/");
        await SkipWelcomeAsync(page);

        // Default tab is Overview. Sub-nav should show the Overview panels.
        var initialSubnav = await page.Locator("#subTabNav .subtab-link").CountAsync();
        initialSubnav.Should().BeGreaterThan(0, "Overview tab should have at least one panel in the sub-nav");

        // Switch to Identity. Sub-nav should rebuild with Identity panels (4: directory, users, groups, devices).
        await page.Locator("[data-tab-link='identity']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.body.dataset.activeTab === 'identity' && document.querySelectorAll('#subTabNav .subtab-link').length > 0");
        var identitySubnav = await page.Locator("#subTabNav .subtab-link").CountAsync();
        identitySubnav.Should().Be(4, "Identity has 4 panels (directory, users, groups, devices)");

        // Active sub-tab should match the active panel. No more than one panel should be visible at a time.
        var visiblePanelIds = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('section.panel[data-tab=\\'identity\\']:not(.subtab-hidden)')].map(p => p.id)");
        visiblePanelIds.Should().HaveCount(1, "exactly one Identity panel should be visible at a time");
    }

    [Fact]
    public async Task Settings_drawer_opens_via_gear_and_closes_via_escape()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{server.BaseUrl}/admin/");
        await SkipWelcomeAsync(page);

        // Drawer is closed at load.
        var drawerOpenBefore = await page.Locator("#settingsDrawer").GetAttributeAsync("data-open");
        drawerOpenBefore.Should().Be("false");

        // Click gear -> drawer opens, status + license panels visible.
        await page.Locator("#settingsTrigger").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('settingsDrawer')?.dataset.open === 'true'");

        var statusPanelHeight = await page.Locator("#status").EvaluateAsync<int>("e => e.offsetHeight");
        var licensePanelHeight = await page.Locator("#license").EvaluateAsync<int>("e => e.offsetHeight");
        statusPanelHeight.Should().BeGreaterThan(0, "status panel should be rendered in the open drawer");
        licensePanelHeight.Should().BeGreaterThan(0, "license panel should be rendered in the open drawer");

        // Press Escape -> drawer closes.
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('settingsDrawer')?.dataset.open === 'false'");
    }

    [Fact]
    public async Task Share_link_with_prefilled_query_params_collapses_advanced_details()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var url =
            $"{server.BaseUrl}/share/?tenantId=aaaaaaaa-1111-2222-3333-cccccccccccc"
            + "&accessToken=ABC123TOKEN&guestEmail=bob@guest.test";
        await page.GotoAsync(url);

        // Tenant + token + email all loaded from URL.
        var tenant = await page.Locator("#tenantId").InputValueAsync();
        var token = await page.Locator("#accessToken").InputValueAsync();
        var email = await page.Locator("#guestEmail").InputValueAsync();
        tenant.Should().Be("aaaaaaaa-1111-2222-3333-cccccccccccc");
        token.Should().Be("ABC123TOKEN");
        email.Should().Be("bob@guest.test");

        // Advanced disclosure should auto-collapse so the recipient sees a minimal form.
        var detailsOpen = await page.Locator("#shareDetailsAdvanced")
            .EvaluateAsync<bool>("e => e.open");
        detailsOpen.Should().BeFalse("share details auto-collapse when URL prefilled them all");

        // Badge should signal the load happened.
        var badgeHidden = await page.Locator("#prefillBadge")
            .EvaluateAsync<bool>("e => e.hidden");
        badgeHidden.Should().BeFalse("the ✓ loaded badge should show after URL prefill");

        // Status line should mention the recipient email.
        var status = await page.Locator("#viewerStatus").InnerTextAsync();
        status.Should().Contain("bob@guest.test");
    }

    /// <summary>
    /// Walks past the welcome screen by setting the localStorage flag and the
    /// pre-populated tenant ID, then reloading. Mirrors the "I already have
    /// credentials" path so the rest of the test can focus on the actual flow.
    /// </summary>
    private async Task SkipWelcomeAsync(IPage page)
    {
        await page.EvaluateAsync(
            "() => { "
            + "localStorage.setItem('drm:bootstrapped', '1'); "
            + "localStorage.setItem('drm:tenantId', '00000000-1111-2222-3333-444444444444'); "
            + "localStorage.setItem('drm:adminUserId', '00000000-aaaa-bbbb-cccc-dddddddddddd'); "
            + "}");
        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('welcomeScreen')?.hidden === true");
    }
}
