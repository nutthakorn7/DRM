namespace Drm.Server;

[Flags]
public enum LicenseTier
{
    None = 0,
    Standard = 1 << 0,
    Api = 1 << 1,
    NetFolder = 1 << 2,
    RemoteDelete = 1 << 3,
    Mobile = 1 << 4,
    Box = 1 << 5,
    Outlook = 1 << 6,
    All = Standard | Api | NetFolder | RemoteDelete | Mobile | Box | Outlook
}

public static class LicenseTierParser
{
    public static LicenseTier ParseConfigured(string? configValue)
    {
        if (string.IsNullOrWhiteSpace(configValue))
        {
            return LicenseTier.All;
        }

        if (string.Equals(configValue.Trim(), "All", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseTier.All;
        }

        var tier = LicenseTier.None;
        foreach (var token in configValue.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<LicenseTier>(token.Trim(), ignoreCase: true, out var parsed))
            {
                tier |= parsed;
            }
        }
        return tier == LicenseTier.None ? LicenseTier.All : tier;
    }
}
