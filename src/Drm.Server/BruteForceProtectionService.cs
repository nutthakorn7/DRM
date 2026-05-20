using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Centralised brute-force detection for external share links. Callers record
/// every failed access attempt; the service decides when to auto-revoke based
/// on the tenant's configured threshold + window.
///
/// Defaults (when no <see cref="TenantBruteForcePolicyEntity"/> row exists):
/// <list type="bullet">
///   <item>Enabled = true</item>
///   <item>Threshold = 10 failures</item>
///   <item>Window = 60 minutes</item>
/// </list>
///
/// The service writes the failure row, counts failures within the window, and
/// — if the threshold is reached — flips the share link's <c>Revoked</c> flag
/// and stamps <c>RevocationReason = "brute_force_threshold"</c>. Callers must
/// still call <see cref="AppDbContext.SaveChangesAsync"/> to commit.
/// </summary>
public sealed class BruteForceProtectionService(AppDbContext dbContext, IAdminNotificationService notificationService)
{
    public const string RevocationReason = "brute_force_threshold";

    /// <summary>
    /// Records a failed access attempt against a share link and, if the
    /// tenant's threshold is reached within the configured window, revokes
    /// the share link in-place on the tracked entity. Returns true when the
    /// share link was auto-revoked by THIS call so the caller can write the
    /// matching audit event and notification.
    /// </summary>
    public async Task<bool> RecordFailedAttemptAsync(
        ExternalShareLinkEntity shareLink,
        string guestEmail,
        string? ipAddress,
        string reasonCode,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        // Always record the failure — even when the link is already revoked.
        // The audit trail wants to see "they kept trying after we shut them out".
        dbContext.ShareLinkFailedAttempts.Add(new ShareLinkFailedAttemptEntity
        {
            TenantId = shareLink.TenantId,
            ShareLinkId = shareLink.ShareLinkId,
            GuestEmail = guestEmail ?? string.Empty,
            IpAddress = ipAddress,
            ReasonCode = reasonCode,
            OccurredAtUtc = occurredAtUtc
        });

        // Already revoked — no need to evaluate the threshold again.
        if (shareLink.Revoked)
        {
            return false;
        }

        var policy = await dbContext.TenantBruteForcePolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.TenantId == shareLink.TenantId, cancellationToken);

        var enabled = policy?.Enabled ?? true;
        var threshold = policy?.Threshold ?? 10;
        var windowMinutes = policy?.WindowMinutes ?? 60;

        if (!enabled || threshold <= 0 || windowMinutes <= 0)
        {
            return false;
        }

        // SQLite's DateTimeOffset translation in WHERE/ORDER is known
        // unreliable in this codebase, so we pull the share link's rows
        // and filter in-memory. Bounded by share link, not tenant-wide,
        // so the result set stays small even on hot links.
        var windowStart = occurredAtUtc.AddMinutes(-windowMinutes);
        var attempts = await dbContext.ShareLinkFailedAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.TenantId == shareLink.TenantId &&
                attempt.ShareLinkId == shareLink.ShareLinkId)
            .ToListAsync(cancellationToken);
        var recentFailureCount = attempts.Count(a => a.OccurredAtUtc >= windowStart);

        // +1 because we just queued an attempt that isn't committed yet.
        if (recentFailureCount + 1 < threshold)
        {
            return false;
        }

        // Threshold hit — revoke the link.
        shareLink.Revoked = true;
        shareLink.RevokedAtUtc = occurredAtUtc;
        shareLink.RevocationReason = RevocationReason;

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = shareLink.TenantId,
            FileId = shareLink.FileId,
            UserId = null,
            EventType = "share_link_auto_revoked",
            ReasonCode = RevocationReason,
            CreatedAtUtc = occurredAtUtc
        });

        await notificationService.NotifyAsync(
            shareLink.TenantId,
            new AdminNotificationEvent(
                "share_link_auto_revoked",
                shareLink.FileId,
                null,
                null,
                occurredAtUtc,
                ReasonCode: RevocationReason),
            cancellationToken);

        return true;
    }
}
