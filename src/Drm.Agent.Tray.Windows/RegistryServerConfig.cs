using Microsoft.Win32;

namespace Drm.Agent.Tray.Windows;

/// <summary>
/// Reads HKLM\SOFTWARE\zcrDRM\ServerUrl, set by the MSI at install
/// time so the tray never has to ask the end user for the server URL.
///
/// If the registry key is missing (e.g. the user side-loaded the
/// publish output instead of running the MSI), TryRead returns null
/// and the tray falls back to its usual textbox-driven flow.
///
/// 64-bit MSI lays the key down in the 64-bit view; the agent is built
/// for AnyCPU but launches as a 64-bit process on win-x64.  Forcing
/// RegistryView.Registry64 sidesteps WoW64 redirection on the rare
/// 32-bit-emulation install.
/// </summary>
public static class RegistryServerConfig
{
    public const string RegistryRootKey = @"SOFTWARE\zcrDRM";
    public const string ServerUrlValueName = "ServerUrl";

    /// <summary>
    /// Reads the configured server URL. Returns null when no MSI has
    /// run (no registry key) or when the value is unparseable as an
    /// absolute URI.  Never throws — callers handle the null path.
    /// </summary>
    public static Uri? TryReadServerUrl()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(RegistryRootKey, writable: false);
            if (key is null)
            {
                return null;
            }

            var value = key.GetValue(ServerUrlValueName) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ? uri : null;
        }
        catch (System.Security.SecurityException)
        {
            // Unprivileged session with a locked-down HKLM read ACL —
            // shouldn't happen on a normal Windows install, but
            // returning null lets the tray fall back gracefully.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
