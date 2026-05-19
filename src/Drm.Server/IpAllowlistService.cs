using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Checks whether an incoming request IP address is permitted by the tenant's
/// IP allowlist. When no rules are configured for a tenant, all IPs are allowed
/// (allowlist is opt-in, not deny-by-default).
/// </summary>
public static class IpAllowlistService
{
    /// <summary>
    /// Returns <c>true</c> if the request should be denied (i.e. an allowlist
    /// exists for the tenant and the client IP is not in it).
    /// </summary>
    public static async Task<bool> IsDeniedAsync(
        AppDbContext dbContext,
        Guid tenantId,
        string? clientIp,
        CancellationToken ct)
    {
        var rules = await dbContext.TenantIpAllowlistRules
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.Cidr)
            .ToListAsync(ct);

        // No rules configured → allow everything (opt-in model)
        if (rules.Count == 0)
            return false;

        if (string.IsNullOrWhiteSpace(clientIp))
            return true; // allowlist enforced but no IP to verify

        // Normalize IPv4-mapped IPv6 (e.g. "::ffff:127.0.0.1" → "127.0.0.1")
        if (!IPAddress.TryParse(clientIp, out var clientAddress))
            return true;

        if (clientAddress.IsIPv4MappedToIPv6)
            clientAddress = clientAddress.MapToIPv4();

        foreach (var cidr in rules)
        {
            try
            {
                var network = IPNetwork.Parse(cidr);
                if (network.Contains(clientAddress))
                    return false; // matched an allowlisted range
            }
            catch (FormatException)
            {
                // skip malformed CIDR — don't let bad data block everyone
            }
        }

        return true; // no allowlisted range matched
    }

    /// <summary>
    /// Validates that a CIDR string is syntactically correct.
    /// </summary>
    public static bool IsValidCidr(string cidr)
    {
        try
        {
            IPNetwork.Parse(cidr);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
