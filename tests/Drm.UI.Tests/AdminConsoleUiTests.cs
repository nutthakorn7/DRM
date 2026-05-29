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

        // Switching to Identity must rebuild the sub-nav to EXACTLY its own
        // panels — one link per panel, with no stale links left over from the
        // previous (Overview) tab. Derive the expectation from the DOM rather
        // than hard-coding a count: this assertion previously hard-coded 4 and
        // silently went stale when a 5th Identity panel (adminIdentity) was
        // added, so the panel set itself is the source of truth here.
        var expectedIdentityPanelIds = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('section.panel[data-tab=\\'identity\\']')].map(p => p.id).sort()");
        expectedIdentityPanelIds.Length.Should().BeGreaterThan(1,
            "Identity should have multiple panels for the sub-nav to be meaningful");

        await page.Locator("[data-tab-link='identity']").ClickAsync();

        // Wait for the sub-nav to SETTLE to exactly the Identity panel set —
        // comparing the full id set (not just length > 0) means we can't read a
        // half-rebuilt sub-nav that still holds Overview links.
        await page.WaitForFunctionAsync(
            @"(expected) => document.body.dataset.activeTab === 'identity'
                && JSON.stringify([...document.querySelectorAll('#subTabNav .subtab-link')]
                        .map(b => b.dataset.subtab).sort()) === JSON.stringify(expected)",
            expectedIdentityPanelIds);

        var identitySubtabIds = await page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('#subTabNav .subtab-link')].map(b => b.dataset.subtab).sort()");
        identitySubtabIds.Should().Equal(expectedIdentityPanelIds,
            "the sub-nav must hold exactly one link per Identity panel — no leftovers from the previous tab, no missing panels");

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

    [Fact]
    public async Task Admin_console_at_375px_renders_with_collapsed_rail_and_no_h1_wrap()
    {
        await using var context = await playwright.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new() { Width = 375, Height = 812 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{server.BaseUrl}/admin/");
        await SkipWelcomeAsync(page);

        // Workspace grid collapses the rail to an icon-only column on mobile.
        // (Either the mobile @media 56px rule or the data-rail-collapsed 64px
        // rule wins depending on CSS specificity — both count as "icon only".)
        var railWidth = await page.Locator(".rail").EvaluateAsync<int>("e => e.offsetWidth");
        railWidth.Should().BeLessThan(100, "rail must collapse to icon-only width on mobile");

        // Page-header h1 should be exactly one line (line-height ~1.2 × 20px ≈ 24px).
        var h1Height = await page.Locator(".page-header .brand-text h1")
            .EvaluateAsync<int>("e => e.offsetHeight");
        h1Height.Should().BeLessThan(30, "h1 must fit on one line on a 375px viewport");

        // "Admin console" subtitle should hide on mobile to free horizontal space.
        var hintHeight = await page.Locator(".page-header .brand-text .hint")
            .EvaluateAsync<int>("e => e.offsetHeight");
        hintHeight.Should().Be(0, "admin-console hint is hidden on mobile");
    }

    [Fact]
    public async Task Me_send_form_is_immediately_usable_with_no_blocking_modals()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{server.BaseUrl}/me/");

        // No persona modal, no tour overlay on first visit.
        var modalCount = await page.Locator(".role-picker-overlay, .tour-overlay").CountAsync();
        modalCount.Should().Be(0, "/me/ should not greet a new user with any blocking modal");

        // The send form is rendered and the Personalize escape hatch is present.
        var dropZoneHeight = await page.Locator("#dropZone").EvaluateAsync<int>("e => e.offsetHeight");
        dropZoneHeight.Should().BeGreaterThan(0, "drop zone is visible on first visit");

        var personalizeVisible = await page.Locator("#personalizeLink").IsVisibleAsync();
        personalizeVisible.Should().BeTrue("Personalize topbar link is the opt-in path for the persona picker");
    }

    [Fact]
    public async Task Share_viewer_step2_form_is_pointer_events_none_until_step1_completes()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{server.BaseUrl}/share/");

        // On a fresh load (no JS-driven success yet), step 2 is muted + un-clickable.
        var step2 = page.Locator("#confirmStep");
        var pointerEvents = await step2.EvaluateAsync<string>("e => getComputedStyle(e).pointerEvents");
        pointerEvents.Should().Be("none", "step 2 must be pointer-events:none until step 1 success");

        var opacity = await step2.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).opacity)");
        opacity.Should().BeLessThan(0.6, "step 2 must look visually muted");

        // Simulate the step-1 success transition that the JS does, then assert step 2
        // becomes interactive. We don't actually call the API — we just flip the
        // class the way startVerification() does — because that's the bit the UI
        // contract guarantees.
        await page.EvaluateAsync(
            "() => { document.getElementById('startStep').classList.add('complete'); "
            + "document.getElementById('confirmStep').classList.add('active'); }");

        var pointerEventsAfter = await step2.EvaluateAsync<string>("e => getComputedStyle(e).pointerEvents");
        pointerEventsAfter.Should().Be("auto", "step 2 becomes interactive once it has .active");
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
