namespace Drm.Server;

/// <summary>
/// Decision 1 (CRUD audit 2026-06-12): retention DEPERSONALIZES audit events past
/// the window instead of deleting them.
///
/// The HMAC chain hashes only Id/TenantId/EventType/CreatedAtUtc (see
/// <see cref="AuditChainService"/>), so nulling the personal columns
/// (UserId/FileId/ActorAdminId) and clearing ReasonCode removes the personal data
/// for PDPA WITHOUT changing any hash — the tamper-evident chain stays fully
/// verifiable. Deleting rows instead would break verification at the new genesis
/// (the verifier seeds PrevHash = Sentinel from the first surviving event).
/// </summary>
public static class AuditRetentionService
{
    /// <summary>
    /// Nulls the personal fields on audit events older than <paramref name="cutoff"/>
    /// that still carry any. Returns the number of rows depersonalized.
    /// </summary>
    public static async Task<int> DepersonalizeAsync(
        AppDbContext dbContext, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        // Materialize first, then filter in memory — SQLite DateTimeOffset WHERE
        // clauses are unreliable (same workaround as DataRetentionService). The
        // job runs once per day so the read is acceptable.
        var all = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(dbContext.AuditEvents, ct);

        var stale = all
            .Where(e => e.CreatedAtUtc < cutoff
                && (e.UserId is not null
                    || e.FileId is not null
                    || e.ActorAdminId is not null
                    || e.ReasonCode.Length > 0))
            .ToList();

        if (stale.Count == 0)
        {
            return 0;
        }

        foreach (var auditEvent in stale)
        {
            auditEvent.UserId = null;
            auditEvent.FileId = null;
            auditEvent.ActorAdminId = null;
            auditEvent.ReasonCode = string.Empty;
        }

        await dbContext.SaveChangesAsync(ct);
        return stale.Count;
    }
}
