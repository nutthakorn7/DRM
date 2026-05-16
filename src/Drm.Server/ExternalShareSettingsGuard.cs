using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public static class ExternalShareSettingsGuard
{
    private const int MaxAllowedGuestDomains = 200;
    private const int MaxBlockedGuestEmails = 1000;
    public const int MaxShareLinkLifetimeHoursLimit = 24 * 365;
    public const int MaxShareLinkMaxUsesLimit = 1000;
    public const int MaxActiveShareLinksPerFileLimit = 1000;

    public static async Task<TenantExternalShareSettingsEntity?> GetSettingsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await dbContext.TenantExternalShareSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId, cancellationToken);
    }

    public static bool IsExternalSharingEnabled(TenantExternalShareSettingsEntity? settings)
    {
        return settings?.ExternalSharingEnabled ?? true;
    }

    public static async Task<bool> IsExternalSharingEnabledAsync(
        AppDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(dbContext, tenantId, cancellationToken);
        return IsExternalSharingEnabled(settings);
    }

    public static bool IsGuestEmailDomainAllowed(TenantExternalShareSettingsEntity? settings, string guestEmail)
    {
        var allowedDomains = ParseAllowedDomains(settings?.AllowedGuestEmailDomainsCsv);
        if (allowedDomains.Count == 0)
        {
            return true;
        }

        var domain = TryExtractGuestEmailDomain(guestEmail);
        return domain is not null && allowedDomains.Contains(domain);
    }

    public static bool IsGuestEmailBlocked(TenantExternalShareSettingsEntity? settings, string guestEmail)
    {
        var blockedEmails = ParseBlockedGuestEmails(settings?.BlockedGuestEmailsCsv);
        if (blockedEmails.Count == 0)
        {
            return false;
        }

        var normalizedGuestEmail = NormalizeEmail(guestEmail);
        return IsValidGuestEmail(normalizedGuestEmail) && blockedEmails.Contains(normalizedGuestEmail);
    }

    public static bool TryNormalizeAllowedGuestEmailDomainsCsv(
        string? allowedGuestEmailDomainsCsv,
        out string normalizedCsv)
    {
        normalizedCsv = string.Empty;
        if (string.IsNullOrWhiteSpace(allowedGuestEmailDomainsCsv))
        {
            return true;
        }

        var normalizedDomains = new SortedSet<string>(StringComparer.Ordinal);
        var candidates = allowedGuestEmailDomainsCsv
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in candidates)
        {
            var normalizedDomain = NormalizeDomain(candidate);
            if (!IsValidDomain(normalizedDomain))
            {
                return false;
            }

            normalizedDomains.Add(normalizedDomain);
            if (normalizedDomains.Count > MaxAllowedGuestDomains)
            {
                return false;
            }
        }

        normalizedCsv = string.Join(",", normalizedDomains);
        return true;
    }

    public static bool TryNormalizeBlockedGuestEmailsCsv(
        string? blockedGuestEmailsCsv,
        out string normalizedCsv)
    {
        normalizedCsv = string.Empty;
        if (string.IsNullOrWhiteSpace(blockedGuestEmailsCsv))
        {
            return true;
        }

        var normalizedEmails = new SortedSet<string>(StringComparer.Ordinal);
        var candidates = blockedGuestEmailsCsv
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in candidates)
        {
            var normalizedEmail = NormalizeEmail(candidate);
            if (!IsValidGuestEmail(normalizedEmail))
            {
                return false;
            }

            normalizedEmails.Add(normalizedEmail);
            if (normalizedEmails.Count > MaxBlockedGuestEmails)
            {
                return false;
            }
        }

        normalizedCsv = string.Join(",", normalizedEmails);
        return true;
    }

    public static bool IsValidMaxShareLinkLifetimeHours(int? value)
    {
        return value is null || (value.Value >= 1 && value.Value <= MaxShareLinkLifetimeHoursLimit);
    }

    public static bool IsValidMaxShareLinkMaxUses(int? value)
    {
        return value is null || (value.Value >= 1 && value.Value <= MaxShareLinkMaxUsesLimit);
    }

    public static bool IsValidMaxActiveShareLinksPerFile(int? value)
    {
        return value is null || (value.Value >= 1 && value.Value <= MaxActiveShareLinksPerFileLimit);
    }

    public static bool IsShareLinkLifetimeAllowed(
        TenantExternalShareSettingsEntity? settings,
        DateTimeOffset now,
        DateTimeOffset expiresAtUtc)
    {
        var maxLifetimeHours = settings?.MaxShareLinkLifetimeHours;
        return maxLifetimeHours is null || expiresAtUtc <= now.AddHours(maxLifetimeHours.Value);
    }

    public static bool IsShareLinkMaxUsesAllowed(TenantExternalShareSettingsEntity? settings, int maxUses)
    {
        var maxShareLinkMaxUses = settings?.MaxShareLinkMaxUses;
        return maxShareLinkMaxUses is null || maxUses <= maxShareLinkMaxUses.Value;
    }

    public static bool IsActiveShareLinkCountAllowed(TenantExternalShareSettingsEntity? settings, int activeShareLinksPerFile)
    {
        var maxActiveShareLinksPerFile = settings?.MaxActiveShareLinksPerFile;
        return maxActiveShareLinksPerFile is null || activeShareLinksPerFile <= maxActiveShareLinksPerFile.Value;
    }

    private static HashSet<string> ParseAllowedDomains(string? csv)
    {
        var domains = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv))
        {
            return domains;
        }

        var candidates = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in candidates)
        {
            var normalizedDomain = NormalizeDomain(candidate);
            if (IsValidDomain(normalizedDomain))
            {
                domains.Add(normalizedDomain);
            }
        }

        return domains;
    }

    private static HashSet<string> ParseBlockedGuestEmails(string? csv)
    {
        var emails = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv))
        {
            return emails;
        }

        var candidates = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in candidates)
        {
            var normalizedEmail = NormalizeEmail(candidate);
            if (IsValidGuestEmail(normalizedEmail))
            {
                emails.Add(normalizedEmail);
            }
        }

        return emails;
    }

    private static string? TryExtractGuestEmailDomain(string guestEmail)
    {
        var normalizedEmail = (guestEmail ?? string.Empty).Trim().ToLowerInvariant();
        var atIndex = normalizedEmail.IndexOf('@');
        if (atIndex <= 0 || atIndex != normalizedEmail.LastIndexOf('@') || atIndex == normalizedEmail.Length - 1)
        {
            return null;
        }

        return normalizedEmail[(atIndex + 1)..];
    }

    private static string NormalizeDomain(string domain)
    {
        var normalized = (domain ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith('@'))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }

    private static string NormalizeEmail(string email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool IsValidGuestEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        return email.Length is >= 3 and <= 320
            && atIndex > 0
            && atIndex == email.LastIndexOf('@')
            && atIndex < email.Length - 1
            && email.IndexOfAny([' ', '\t', '\r', '\n']) < 0;
    }

    private static bool IsValidDomain(string domain)
    {
        if (domain.Length is < 1 or > 253)
        {
            return false;
        }

        if (domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var labels = domain.Split('.', StringSplitOptions.None);
        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63)
            {
                return false;
            }

            if (!char.IsLetterOrDigit(label[0]) || !char.IsLetterOrDigit(label[^1]))
            {
                return false;
            }

            foreach (var character in label)
            {
                if (!char.IsLetterOrDigit(character) && character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }
}
