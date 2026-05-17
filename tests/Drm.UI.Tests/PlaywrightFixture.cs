using Microsoft.Playwright;

namespace Drm.UI.Tests;

/// <summary>
/// Shared Playwright browser fixture. Launches Chromium once for the test
/// session; each test gets its own browser context + page so cookies and
/// localStorage are isolated.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        Playwright?.Dispose();
    }
}

/// <summary>
/// xUnit collection that pairs the long-lived Drm.Server process with the
/// long-lived Playwright browser. All UI tests share these fixtures.
/// </summary>
[CollectionDefinition(nameof(DrmUiTestCollection))]
public sealed class DrmUiTestCollection
    : ICollectionFixture<DrmServerFixture>, ICollectionFixture<PlaywrightFixture>
{
}
