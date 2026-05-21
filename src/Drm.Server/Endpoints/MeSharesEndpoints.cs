using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

/// <summary>
/// Stage 18 — sender-side "My Shares" view. Lets a regular user
/// see who they've shared files with, when, how many opens are
/// left, and whether the link is still live — without bouncing
/// them to the admin console.
///
/// Reuses the same JOIN pattern the admin /api/admin/files uses
/// (ProtectedFiles + ExternalShareLinks), filtered to the
/// caller's UserId so users can only see their own shares.
/// </summary>
public static class MeSharesEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static IEndpointRouteBuilder MapMeSharesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/shares", ListMySharesAsync);
        endpoints.MapPost("/api/me/shares/{shareLinkId:guid}/revoke", RevokeOwnShareAsync);
        return endpoints;
    }

    /// <summary>
    /// Stage 20 — sender-side self-revoke. A user can flip Revoked=true
    /// on any share-link they created (file.OwnerUserId == request.UserId).
    /// Same audit + DB shape as the admin revoke path, just gated on
    /// "your own file" instead of AdminPermissions.FilesRevoke so users
    /// don't need to bug admin to cancel a misclick.
    /// </summary>
    private static async Task<IResult> RevokeOwnShareAsync(
        Guid shareLinkId,
        RevokeOwnShareRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }
        if (request.TenantId == Guid.Empty || request.UserId == Guid.Empty)
        {
            return Results.BadRequest(new ErrorResponse("invalid_identifiers"));
        }

        var shareLink = await dbContext.ExternalShareLinks
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.TenantId == request.TenantId &&
                    candidate.ShareLinkId == shareLinkId,
                cancellationToken);
        if (shareLink is null)
        {
            return Results.NotFound();
        }

        // Ownership guard — user can ONLY revoke shares for files they
        // own. This is the difference from the admin revoke endpoint:
        // no FilesRevoke permission required, but file.OwnerUserId must
        // match the caller. Look up the file and reject if not.
        var file = await dbContext.ProtectedFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                f => f.TenantId == request.TenantId && f.Id == shareLink.FileId,
                cancellationToken);
        if (file is null || file.OwnerUserId != request.UserId)
        {
            // 404 not 403 to avoid leaking "this share exists but isn't
            // yours" — same shape as admin file-not-found.
            return Results.NotFound();
        }

        if (!shareLink.Revoked)
        {
            var now = DateTimeOffset.UtcNow;
            shareLink.Revoked = true;
            shareLink.RevokedAtUtc = now;
            shareLink.RevocationReason = "self_revoked";
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = request.TenantId,
                FileId = shareLink.FileId,
                UserId = request.UserId,
                ActorAdminId = null, // self-revoke is a user action, not admin
                EventType = "external_share_changed",
                ReasonCode = "external_share_link_self_revoked",
                CreatedAtUtc = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new RevokeOwnShareResponse(
            shareLink.TenantId,
            shareLink.ShareLinkId,
            shareLink.FileId,
            shareLink.Revoked,
            shareLink.RevokedAtUtc));
    }

    public sealed record RevokeOwnShareRequest(Guid TenantId, Guid UserId);
    public sealed record RevokeOwnShareResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        bool Revoked,
        DateTimeOffset? RevokedAtUtc);

    private static async Task<Results<Ok<MySharesResponse>, BadRequest<ErrorResponse>>> ListMySharesAsync(
        Guid tenantId,
        Guid userId,
        AppDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int? limit = null)
    {
        if (!httpContext.MatchesHeader(tenantId))
        {
            return TypedResults.BadRequest(new ErrorResponse("tenant_mismatch"));
        }
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }
        var effectiveLimit = limit is null
            ? DefaultLimit
            : Math.Clamp(limit.Value, 1, MaxLimit);

        // Per-row LEFT JOIN of ExternalShareLinks against ProtectedFiles —
        // we want the share row even if the underlying file has been
        // revoked, so the user can see what they shared historically
        // and re-share if needed.
        // SQLite can't ORDER BY on DateTimeOffset directly so the query
        // pulls the (cap-bounded) result set first and we sort + project
        // in memory. With effectiveLimit ≤ MaxLimit (200) per call the
        // memory cost is negligible. Same shape as AdminAuditEndpoints
        // for consistency.
        var raw = await (from share in dbContext.ExternalShareLinks.AsNoTracking()
                         join file in dbContext.ProtectedFiles.AsNoTracking()
                             on new { share.TenantId, FileId = share.FileId }
                             equals new { file.TenantId, FileId = file.Id }
                         where share.TenantId == tenantId && file.OwnerUserId == userId
                         select new
                         {
                             share.ShareLinkId,
                             share.FileId,
                             file.ContentType,
                             share.GuestEmail,
                             share.CreatedAtUtc,
                             share.ExpiresAtUtc,
                             share.MaxUses,
                             share.UsedCount,
                             ShareRevoked = share.Revoked,
                             share.RevokedAtUtc,
                             share.RevocationReason,
                             FileRevoked = file.Revoked,
                             file.Permissions
                         }).ToListAsync(cancellationToken);

        var rows = raw
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(effectiveLimit)
            .Select(r => new MyShareRow(
                r.ShareLinkId,
                r.FileId,
                r.ContentType,
                r.GuestEmail,
                r.CreatedAtUtc,
                r.ExpiresAtUtc,
                r.MaxUses,
                r.UsedCount,
                r.ShareRevoked,
                r.RevokedAtUtc,
                r.RevocationReason,
                r.FileRevoked,
                r.Permissions.ToString()))
            .ToList();

        return TypedResults.Ok(new MySharesResponse(tenantId, userId, rows));
    }
}

internal sealed record ErrorResponse(string ReasonCode);

public sealed record MyShareRow(
    Guid ShareLinkId,
    Guid FileId,
    string ContentType,
    string GuestEmail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaxUses,
    int UsedCount,
    bool ShareRevoked,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason,
    bool FileRevoked,
    string Permissions);

public sealed record MySharesResponse(
    Guid TenantId,
    Guid UserId,
    IReadOnlyList<MyShareRow> Shares);
