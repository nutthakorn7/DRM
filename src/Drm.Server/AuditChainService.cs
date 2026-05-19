using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Appends an HMAC-SHA256 chain entry whenever a new AuditEventEntity is saved.
/// The chain links: Hash = HMAC(PrevHash + Id + TenantId + EventType + CreatedAtUtc-ISO8601).
/// Consumers can verify integrity with GET /api/admin/audit/chain/verify.
/// </summary>
public static class AuditChainService
{
    private const string Sentinel = "0000000000000000000000000000000000000000000000000000000000000000";

    public static async Task AppendAsync(
        AppDbContext dbContext,
        AuditEventEntity auditEvent,
        string hmacKeyHex,
        CancellationToken ct = default)
    {
        // Find the latest chain entry for this tenant to get prevHash
        var allChain = await dbContext.AuditChain.AsNoTracking()
            .ToListAsync(ct);

        // Match to tenant events to find last hash
        var tenantEventIds = await dbContext.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == auditEvent.TenantId && e.Id < auditEvent.Id)
            .Select(e => e.Id)
            .ToListAsync(ct);

        string prevHash = Sentinel;
        if (tenantEventIds.Count > 0)
        {
            var lastChain = allChain
                .Where(c => tenantEventIds.Contains(c.AuditEventId))
                .OrderByDescending(c => c.AuditEventId)
                .FirstOrDefault();
            if (lastChain is not null)
                prevHash = lastChain.Hash;
        }

        var input = $"{prevHash}{auditEvent.Id}{auditEvent.TenantId}{auditEvent.EventType}{auditEvent.CreatedAtUtc:O}";
        var hash = ComputeHmac(input, hmacKeyHex);

        dbContext.AuditChain.Add(new AuditChainEntity
        {
            AuditEventId = auditEvent.Id,
            Hash = hash,
            PrevHash = prevHash
        });
    }

    public static async Task<ChainVerifyResult> VerifyTenantChainAsync(
        AppDbContext dbContext,
        Guid tenantId,
        string hmacKeyHex,
        CancellationToken ct = default)
    {
        var events = await dbContext.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

        if (events.Count == 0)
            return new ChainVerifyResult(true, 0, null);

        var eventIds = events.Select(e => e.Id).ToList();
        var chainEntries = await dbContext.AuditChain.AsNoTracking()
            .Where(c => eventIds.Contains(c.AuditEventId))
            .ToDictionaryAsync(c => c.AuditEventId, ct);

        string expectedPrev = Sentinel;
        foreach (var evt in events)
        {
            if (!chainEntries.TryGetValue(evt.Id, out var entry))
                return new ChainVerifyResult(false, evt.Id, "missing_chain_entry");

            if (entry.PrevHash != expectedPrev)
                return new ChainVerifyResult(false, evt.Id, "prev_hash_mismatch");

            var input = $"{entry.PrevHash}{evt.Id}{evt.TenantId}{evt.EventType}{evt.CreatedAtUtc:O}";
            var expected = ComputeHmac(input, hmacKeyHex);
            if (entry.Hash != expected)
                return new ChainVerifyResult(false, evt.Id, "hash_mismatch");

            expectedPrev = entry.Hash;
        }

        return new ChainVerifyResult(true, events.Count, null);
    }

    private static string ComputeHmac(string input, string keyHex)
    {
        var key = Convert.FromHexString(keyHex.Length == 0 ? "00" : keyHex.PadRight(64, '0')[..64]);
        var bytes = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public sealed record ChainVerifyResult(bool Valid, long EventsChecked, string? FailureReason);
}
