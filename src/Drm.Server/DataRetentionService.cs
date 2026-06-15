using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public static class DataRetentionService
{
    /// <summary>
    /// Deletes protected files for a tenant whose ExpiresAtUtc is older than the retention cutoff.
    /// In dry-run mode, counts candidates without deleting.
    /// Returns (deleted, candidates).
    /// </summary>
    public static async Task<(int Deleted, int Candidates)> ApplyTenantRetentionAsync(
        AppDbContext dbContext, Guid tenantId, int retentionDays, bool dryRun, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        // Materialize first — SQLite DateTimeOffset WHERE clauses are unreliable
        var allFiles = await dbContext.ProtectedFiles
            .Where(f => f.TenantId == tenantId)
            .ToListAsync(ct);

        var candidates = allFiles.Where(f => f.ExpiresAtUtc < cutoff).ToList();

        if (dryRun || candidates.Count == 0)
            return (0, candidates.Count);

        // No FK/cascade is configured (every table is an island), so the children
        // must be removed by hand or retention orphans them. This both crypto-shreds
        // the keys of purged files and clears the grant/tag/access-count/collection
        // rows that would otherwise dangle. Materialize-then-remove (SQLite-safe).
        var fileIds = candidates.Select(f => f.Id).ToList();

        var keys = await dbContext.FileKeys
            .Where(k => k.TenantId == tenantId && fileIds.Contains(k.FileId)).ToListAsync(ct);
        var grants = await dbContext.FileGrants
            .Where(g => g.TenantId == tenantId && fileIds.Contains(g.FileId)).ToListAsync(ct);
        var tags = await dbContext.FileTags
            .Where(t => t.TenantId == tenantId && fileIds.Contains(t.FileId)).ToListAsync(ct);
        var accessCounts = await dbContext.FileAccessCounts
            .Where(a => a.TenantId == tenantId && fileIds.Contains(a.FileId)).ToListAsync(ct);
        var collectionItems = await dbContext.FileCollectionItems
            .Where(i => i.TenantId == tenantId && fileIds.Contains(i.FileId)).ToListAsync(ct);

        dbContext.FileKeys.RemoveRange(keys);
        dbContext.FileGrants.RemoveRange(grants);
        dbContext.FileTags.RemoveRange(tags);
        dbContext.FileAccessCounts.RemoveRange(accessCounts);
        dbContext.FileCollectionItems.RemoveRange(collectionItems);
        dbContext.ProtectedFiles.RemoveRange(candidates);
        await dbContext.SaveChangesAsync(ct);
        return (candidates.Count, candidates.Count);
    }
}
