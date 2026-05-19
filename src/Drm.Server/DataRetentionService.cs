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

        dbContext.ProtectedFiles.RemoveRange(candidates);
        await dbContext.SaveChangesAsync(ct);
        return (candidates.Count, candidates.Count);
    }
}
