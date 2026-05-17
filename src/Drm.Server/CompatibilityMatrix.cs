namespace Drm.Server;

/// <summary>
/// Curated compatibility matrix for the DRM viewer / agent against major
/// document, design, and CAD applications. The matrix is intentionally
/// shipped in code (not a database table) so it ships with the binary and
/// updates land via review-able git changes.
/// </summary>
public static class CompatibilityMatrix
{
    public static IReadOnlyList<CompatibilityCategory> Categories { get; } = new[]
    {
        new CompatibilityCategory(
            "Documents",
            new[]
            {
                new CompatibilityEntry("Microsoft Word",            "2019/2021/2024",                "verified", null),
                new CompatibilityEntry("Microsoft Excel",           "2019/2021/2024",                "verified", null),
                new CompatibilityEntry("Microsoft PowerPoint",      "2019/2021/2024",                "verified", null),
                new CompatibilityEntry("Adobe Acrobat Reader DC",   "2024",                          "verified", null),
                new CompatibilityEntry("Adobe Acrobat Pro DC",      "2024",                          "verified", null),
                new CompatibilityEntry("JustSystems Ichitaro",      "Pro 4 / 2024",                  "verified", null),
                new CompatibilityEntry("Fujifilm BI DocuWorks",     "Viewer 9.x / 10.x",             "verified", null),
                new CompatibilityEntry("WordPad",                   "Windows 11",                    "verified", null),
                new CompatibilityEntry("Notepad",                   "Windows 11",                    "verified", null)
            }),
        new CompatibilityCategory(
            "Design / Image",
            new[]
            {
                new CompatibilityEntry("Adobe Illustrator",         "CC 2024",                       "verified", null),
                new CompatibilityEntry("Adobe Photoshop",           "CC 2024",                       "verified", null),
                new CompatibilityEntry("Adobe InDesign",            "CC 2024",                       "verified", null),
                new CompatibilityEntry("Microsoft Paint",           "Windows 11",                    "verified", null)
            }),
        new CompatibilityCategory(
            "CAD",
            new[]
            {
                new CompatibilityEntry("Autodesk AutoCAD",           "2020 / 2021 / 2024 / 2025",    "verified", null),
                new CompatibilityEntry("Autodesk AutoCAD LT",        "2020 / 2021 / 2024 / 2025",    "verified", null),
                new CompatibilityEntry("Autodesk DWG TrueView",      "2020 / 2021",                  "verified", null),
                new CompatibilityEntry("ZWCAD",                      "2020",                         "verified", null),
                new CompatibilityEntry("Dassault SolidWorks",        "2024 / 2025",                  "verified", null),
                new CompatibilityEntry("Dassault SolidWorks Composer Player",
                                                                     "2019 / 2020",                  "verified", null),
                new CompatibilityEntry("Dassault eDrawings",         "2019 / 2020",                  "verified", null),
                new CompatibilityEntry("Siemens Solid Edge",         "2022",                         "verified", null),
                new CompatibilityEntry("Cadence OrCad Capture CIS",  "17.4",                         "verified", null),
                new CompatibilityEntry("Lattice XVL Studio Pro",     "19.0 / 18.1a",                 "verified", null),
                new CompatibilityEntry("Lattice XVL Player",         "20.3 / 20.0a",                 "verified", null),
                new CompatibilityEntry("iCAD SX",                    "V7L7 / V8L1 / V8L2",           "verified", null),
                new CompatibilityEntry("FILDER Cube",                "V1.62",                        "verified", null),
                new CompatibilityEntry("iCADMX",                     "V7L6",                         "verified", null)
            }),
        new CompatibilityCategory(
            "Video",
            new[]
            {
                new CompatibilityEntry("Windows Media Player", "wma, wmv, avi, mpg, mpeg, mp3, mp4", "verified", null)
            }),
        new CompatibilityCategory(
            "Simulation",
            new[]
            {
                new CompatibilityEntry("MathWorks Simulink",         "R2015aSP1",                    "verified", null)
            }),
        new CompatibilityCategory(
            "Known issues",
            new[]
            {
                new CompatibilityEntry("Adobe InCopy",        "any",
                    "broken",
                    "Embedded link resolution silently fails when the linked Photoshop file lives in a different DRM container. Workaround: keep linked assets in the same secure container."),
                new CompatibilityEntry("AutoCAD LISP plugins", "any",
                    "broken",
                    "LISP plugins that call OS file APIs bypassing the protected handle can fail with 'file not found'. Disable the plugin or open with macros disabled."),
                new CompatibilityEntry("Word Open-and-Repair", "any",
                    "warn",
                    "Word's Open-and-Repair flow rewrites the file on the user's behalf and can strip transparent trailers. The DRM viewer warns the user before opening when this mode is detected.")
            })
    };

    public static IReadOnlyList<CompatibilityEntry> KnownIssues => Categories
        .Single(c => c.Name == "Known issues")
        .Entries;

    public static bool IsBlocked(string contentType, out CompatibilityEntry? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        // Currently no content types are hard-blocked; the matrix surfaces
        // application-level guidance only. The hook is in place for future
        // CVE-grade blockers.
        return false;
    }
}

public sealed record CompatibilityCategory(string Name, IReadOnlyList<CompatibilityEntry> Entries);

public sealed record CompatibilityEntry(
    string Application,
    string Versions,
    string Status,
    string? Notes);
