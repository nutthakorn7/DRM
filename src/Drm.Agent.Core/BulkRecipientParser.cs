namespace Drm.Agent.Core;

/// <summary>
/// Stage 19 — parses the Quick Send recipient field into a deduped list
/// of email addresses. Accepts comma, semicolon, or newline as separator
/// so a sender can paste from a spreadsheet column / Outlook contact
/// picker / hand-typed list interchangeably.
///
/// Lives in Drm.Agent.Core (not the WPF tray project) so the parser is
/// cross-platform testable without booting WPF.
/// </summary>
public static class BulkRecipientParser
{
    private static readonly char[] Separators = [',', ';', '\r', '\n', '\t'];

    public static IReadOnlyList<string> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
