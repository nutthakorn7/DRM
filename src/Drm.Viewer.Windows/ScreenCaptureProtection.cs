using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Drm.Viewer.Windows;

/// <summary>
/// Wraps the Windows <c>SetWindowDisplayAffinity</c> API to mark the viewer
/// window as excluded from screen capture and screen sharing. When applied:
///
/// <list type="bullet">
///   <item>The window content appears as a solid black rectangle in
///         Snipping Tool, Win+Shift+S, Print Screen captures, Windows
///         screen recordings, OBS Studio's display capture, Teams /
///         Zoom / Meet screen-share streams, and similar surfaces.</item>
///   <item>The user CAN still see the window normally on their own
///         monitor — only the capture pipeline is blocked.</item>
/// </list>
///
/// This protection is best-effort. A determined attacker with a physical
/// camera, a hardware capture card on the GPU output, or a kernel-level
/// hook can still get pixels. The goal here is the same as FinalCode's:
/// raise the bar against casual screenshot leaks, not promise mathematical
/// impossibility.
///
/// Requires Windows 10 version 2004 (build 19041) or later for
/// <c>WDA_EXCLUDEFROMCAPTURE</c>. On earlier builds the call falls back
/// to <c>WDA_MONITOR</c> which gives a similar (slightly less complete)
/// effect.
/// </summary>
public static class ScreenCaptureProtection
{
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    /// <summary>
    /// Mark the given window as excluded from external screen capture.
    /// Safe to call before or after the window has been shown — the helper
    /// hooks <c>SourceInitialized</c> to apply the flag once the window has
    /// an HWND. Silent no-op if the API isn't available (older Windows,
    /// remote desktop sessions that disable display affinity, etc.).
    /// </summary>
    public static void Enable(Window window)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));

        // If the HWND already exists, apply immediately. Otherwise defer
        // until SourceInitialized — the HWND doesn't exist before then.
        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            ApplyAffinity(helper.Handle, WDA_EXCLUDEFROMCAPTURE);
            return;
        }

        window.SourceInitialized += OnSourceInitialized;

        static void OnSourceInitialized(object? sender, EventArgs e)
        {
            if (sender is not Window w) return;
            w.SourceInitialized -= OnSourceInitialized;
            var hwnd = new WindowInteropHelper(w).Handle;
            ApplyAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }
    }

    /// <summary>
    /// Remove the capture-exclusion flag from a previously protected window.
    /// Useful for diagnostic surfaces (the help overlay should be capturable
    /// for support screenshots, for example). Not used by the main viewer.
    /// </summary>
    public static void Disable(Window window)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        ApplyAffinity(hwnd, WDA_NONE);
    }

    private static void ApplyAffinity(IntPtr hwnd, uint affinity)
    {
        if (hwnd == IntPtr.Zero) return;
        if (SetWindowDisplayAffinity(hwnd, affinity))
        {
            return;
        }

        var lastError = Marshal.GetLastWin32Error();
        // ERROR_PROC_NOT_FOUND (127) on Windows builds before 2004 means
        // WDA_EXCLUDEFROMCAPTURE isn't supported. Fall back to WDA_MONITOR
        // (1903+, blocks Print Screen and most screen-capture APIs but not
        // GDI-level capture). If that also fails, log via Win32Exception
        // and continue — we still rendered the watermark on screen.
        if (affinity == WDA_EXCLUDEFROMCAPTURE)
        {
            if (SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
            {
                return;
            }
            lastError = Marshal.GetLastWin32Error();
        }

        // Surface failure to a debugger if attached; do NOT throw. A failed
        // protection call should not crash the viewer — the user is already
        // looking at the document.
        System.Diagnostics.Debug.WriteLine(
            $"SetWindowDisplayAffinity failed ({affinity:X8}): {new Win32Exception(lastError).Message}");
    }
}
