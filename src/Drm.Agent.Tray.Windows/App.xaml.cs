using System.Windows;
using Drm.Agent.Core;

namespace Drm.Agent.Tray.Windows;

/// <summary>
/// Tray entry point. Coordinates the first-run handshake (HKLM
/// server URL read + DPAPI identity cache + optional FirstRunDialog)
/// before showing MainWindow.
///
/// Falls back gracefully at every step: a missing registry key, an
/// unreachable server, a user clicking "Skip for now" — none of
/// those stop the user from getting to MainWindow's manual form.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Default server URL when the MSI registry key is missing
    /// (e.g. dev side-load of publish output).  Matches the
    /// "https://drm.zcr.ai" baked in by Product.wxs so users in the
    /// canonical production flow see consistent behaviour.
    /// </summary>
    private const string DefaultServerUrlFallback = "https://drm.zcr.ai";

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // ---- 1. Read server URL: MSI registry first, fallback to constant
        var desktopConfiguration = DesktopAgentConfiguration.Load();
        var serverUrl = desktopConfiguration.ServerUrl
                        ?? RegistryServerConfig.TryReadServerUrl()
                        ?? new Uri(DefaultServerUrlFallback);

        // ---- 2. Read cached identity (DPAPI)
        var identityCache = DpapiIdentityCache.Default();
        AgentIdentityCacheEntry? cached;
        try
        {
            cached = identityCache.ReadAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Corrupt cache is treated as no cache (DpapiIdentityCache
            // already swallows the common CryptographicException, but
            // any other I/O fault here gets handled the same way:
            // re-prompt the dialog, don't crash startup).
            cached = null;
        }

        // ---- 3. If no cached identity, show the FirstRunDialog
        var provisionedIdentity = desktopConfiguration.TryCreateIdentity(cached);

        if (cached is null && provisionedIdentity is null)
        {
            var dialog = new FirstRunDialog(serverUrl);
            if (dialog.ShowDialog() == true && dialog.Result is { } newEntry)
            {
                try
                {
                    identityCache.WriteAsync(newEntry, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    cached = newEntry;
                }
                catch
                {
                    // Best-effort cache write. If it fails the user
                    // can still continue this session using the values
                    // surfaced via the dialog (cached is still set
                    // below from newEntry); only the persistence
                    // across launches is lost.
                    cached = newEntry;
                }
            }
            // dialog.ShowDialog() == false → user clicked Skip; fall
            // through with cached == null. MainWindow shows its
            // manual textbox flow.
        }

        // ---- 4. Open MainWindow, pre-filled from the cached identity
        var mainWindow = new MainWindow(serverUrl, cached, desktopConfiguration);
        mainWindow.Show();
    }
}
