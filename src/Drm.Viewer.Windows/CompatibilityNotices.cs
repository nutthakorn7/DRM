namespace Drm.Viewer.Windows;

/// <summary>
/// Friendly notices the viewer surfaces when a protected file's content type
/// is on the compatibility known-issue list. This is a static mirror of the
/// server's compatibility matrix so the viewer works offline; the server's
/// admin compatibility page is the authoritative source.
/// </summary>
public static class CompatibilityNotices
{
    private static readonly Dictionary<string, string> Notices = new(StringComparer.OrdinalIgnoreCase)
    {
        // No content types are blocked outright in the V1 matrix. Entries
        // here surface as warnings on open. When the server adds new
        // known-issue entries, update this map.
        ["application/vnd.adobe.incopy"] =
            "Adobe InCopy: linked Photoshop assets across DRM containers may fail to resolve. Keep linked assets in the same secure container.",
        ["application/vnd.dwg-lisp"] =
            "AutoCAD LISP plugins that bypass the protected handle can fail with 'file not found'. Open with macros disabled.",
    };

    public static string? ForContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        return Notices.TryGetValue(contentType, out var notice) ? notice : null;
    }
}
