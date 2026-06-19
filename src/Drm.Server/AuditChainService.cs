using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Builds and verifies a per-tenant HMAC-SHA256 hash chain over audit events.
/// Each entry links: Hash = HMAC(PrevHash + Id + TenantId + EventType + CreatedAtUtc-unix-ms).
/// Chain rows are written automatically by <see cref="AuditChainInterceptor"/> on every
/// audit-event insert; <see cref="BackfillAsync"/> seeds rows for events that predate the
/// interceptor. Consumers verify integrity with GET /api/admin/audit/chain/verify.
///
/// The timestamp is folded in as Unix milliseconds (not ISO "O") on purpose: Postgres
/// truncates timestamptz to microseconds and EF does not refresh the entity after insert,
/// so an ISO round-trip would change the rendered fractional seconds and break the hash.
/// Milliseconds survive microsecond truncation, so writer and verifier always agree.
/// </summary>
public static class AuditChainService
{
    private const string Sentinel = "0000000000000000000000000000000000000000000000000000000000000000";

    // Single source of truth for the hash input so the writer and verifier can never drift.
    private static string BuildInput(string prevHash, AuditEventEntity e)
        => $"{prevHash}{e.Id}{e.TenantId}{e.EventType}{e.CreatedAtUtc.ToUnixTimeMilliseconds()}";

    /// <summary>
    /// Appends one chain row for <paramref name="auditEvent"/> (which must already have its Id
    /// assigned). Does NOT save — the caller (interceptor) saves so the next prevHash is visible.
    /// </summary>
    public static async Task AppendAsync(
        AppDbContext dbContext,
        AuditEventEntity auditEvent,
        string hmacKeyHex,
        CancellationToken ct = default)
    {
        var prevHash = await LatestTenantHashAsync(dbContext, auditEvent.TenantId, auditEvent.Id, ct);
        var hash = ComputeHmac(BuildInput(prevHash, auditEvent), hmacKeyHex);

        dbContext.AuditChain.Add(new AuditChainEntity
        {
            AuditEventId = auditEvent.Id,
            Hash = hash,
            PrevHash = prevHash
        });
    }

    // Hash of the most recent already-chained event for this tenant with a smaller Id.
    private static async Task<string> LatestTenantHashAsync(
        AppDbContext dbContext, Guid tenantId, long beforeId, CancellationToken ct)
    {
        var hash = await (from c in dbContext.AuditChain.AsNoTracking()
                          join e in dbContext.AuditEvents.AsNoTracking() on c.AuditEventId equals e.Id
                          where e.TenantId == tenantId && e.Id < beforeId
                          orderby e.Id descending
                          select c.Hash).FirstOrDefaultAsync(ct);
        return hash ?? Sentinel;
    }

    /// <summary>
    /// Idempotently creates chain rows for any audit events that lack one — e.g. events written
    /// before the chain interceptor existed. Single in-memory pass per tenant, one bulk insert (O(n)).
    /// Returns the number of rows created (0 = already fully chained, the steady-state fast path).
    /// </summary>
    public static async Task<int> BackfillAsync(
        AppDbContext dbContext,
        string hmacKeyHex,
        CancellationToken ct = default)
    {
        // Cheap guard: once events and chain rows are 1:1 there is nothing to do (every boot after the first).
        var eventCount = await dbContext.AuditEvents.CountAsync(ct);
        var chainCount = await dbContext.AuditChain.CountAsync(ct);
        if (eventCount == chainCount)
            return 0;

        var events = await dbContext.AuditEvents.AsNoTracking()
            .OrderBy(e => e.TenantId).ThenBy(e => e.Id)
            .ToListAsync(ct);
        var existing = await dbContext.AuditChain.AsNoTracking()
            .ToDictionaryAsync(c => c.AuditEventId, c => c.Hash, ct);

        var created = new List<AuditChainEntity>();
        Guid? currentTenant = null;
        var prevHash = Sentinel;
        foreach (var e in events)
        {
            if (currentTenant != e.TenantId)
            {
                currentTenant = e.TenantId;
                prevHash = Sentinel;
            }

            if (existing.TryGetValue(e.Id, out var existingHash))
            {
                prevHash = existingHash; // already chained — carry its hash forward
                continue;
            }

            var hash = ComputeHmac(BuildInput(prevHash, e), hmacKeyHex);
            created.Add(new AuditChainEntity { AuditEventId = e.Id, Hash = hash, PrevHash = prevHash });
            prevHash = hash;
        }

        if (created.Count > 0)
        {
            dbContext.AuditChain.AddRange(created);
            await dbContext.SaveChangesAsync(ct);
        }

        return created.Count;
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

            var expected = ComputeHmac(BuildInput(entry.PrevHash, evt), hmacKeyHex);
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
