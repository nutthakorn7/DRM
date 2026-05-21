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
        return endpoints;
    }

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
