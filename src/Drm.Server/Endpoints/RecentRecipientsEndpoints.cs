using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class RecentRecipientsEndpoints
{
    public static IEndpointRouteBuilder MapRecentRecipientsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/recent-recipients", GetRecentRecipientsAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<RecentRecipient>>, BadRequest<ErrorResponse>>> GetRecentRecipientsAsync(
        Guid tenantId,
        Guid userId,
        AppDbContext dbContext,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }

        var cap = Math.Clamp(limit ?? 10, 1, 50);

        // Pull every share link the user has created (recent first by ID
        // since SQLite cannot ORDER BY DateTimeOffset). Group client-side
        // by guest email to compute use counts and last-used timestamps.
        var rows = await dbContext.ExternalShareLinks
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Join(
                dbContext.ProtectedFiles.AsNoTracking().Where(f => f.OwnerUserId == userId),
                s => s.FileId,
                f => f.Id,
                (s, f) => new { s.GuestEmail, s.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var grouped = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.GuestEmail))
            .GroupBy(r => r.GuestEmail, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RecentRecipient(
                g.Key,
                g.Count(),
                g.Max(r => r.CreatedAtUtc)))
            .OrderByDescending(r => r.LastSentAtUtc)
            .Take(cap)
            .ToList();

        return TypedResults.Ok<IReadOnlyList<RecentRecipient>>(grouped);
    }

    private sealed record RecentRecipient(string Email, int UseCount, DateTimeOffset LastSentAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
