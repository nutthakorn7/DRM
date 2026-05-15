using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class ExternalShareEndpoints
{
    public static IEndpointRouteBuilder MapExternalShareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/share-links");

        group.MapPost("/redeem", RedeemShareLinkAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<ExternalShareRedemptionResponse>, BadRequest<ErrorResponse>, NotFound>> RedeemShareLinkAsync(
        RedeemExternalShareLinkRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_access_token"));
        }

        var guestEmail = NormalizeEmail(request.GuestEmail);
        if (!IsValidGuestEmail(guestEmail))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_guest_email"));
        }

        var tokenHash = ExternalShareToken.Hash(request.AccessToken.Trim());
        var shareLink = await dbContext.ExternalShareLinks
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.TokenHash == tokenHash,
                cancellationToken);

        if (shareLink is null || !string.Equals(shareLink.GuestEmail, guestEmail, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.NotFound();
        }

        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.Id == shareLink.FileId,
                cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var validationError = ValidateRedeemable(shareLink, file, now);
        if (validationError is not null)
        {
            return TypedResults.BadRequest(validationError);
        }

        shareLink.UsedCount += 1;
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = shareLink.TenantId,
            FileId = shareLink.FileId,
            UserId = null,
            EventType = "external_share_accessed",
            ReasonCode = "external_share_link_redeemed",
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ExternalShareRedemptionResponse(
            shareLink.TenantId,
            shareLink.ShareLinkId,
            shareLink.FileId,
            shareLink.GuestEmail,
            file.ContentType,
            shareLink.ExpiresAtUtc,
            shareLink.MaxUses,
            shareLink.UsedCount,
            "external_share_link_redeemed"));
    }

    private static ErrorResponse? ValidateRedeemable(
        ExternalShareLinkEntity shareLink,
        ProtectedFileEntity file,
        DateTimeOffset now)
    {
        if (shareLink.Revoked)
        {
            return new ErrorResponse("share_link_revoked");
        }

        if (shareLink.ExpiresAtUtc <= now)
        {
            return new ErrorResponse("share_link_expired");
        }

        if (shareLink.UsedCount >= shareLink.MaxUses)
        {
            return new ErrorResponse("share_link_max_uses_exceeded");
        }

        if (file.Revoked)
        {
            return new ErrorResponse("file_revoked");
        }

        if (file.ExpiresAtUtc <= now)
        {
            return new ErrorResponse("file_expired");
        }

        return null;
    }

    private static bool IsValidGuestEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        return email.Length is >= 3 and <= 320
            && atIndex > 0
            && atIndex == email.LastIndexOf('@')
            && atIndex < email.Length - 1
            && email.IndexOfAny([' ', '\t', '\r', '\n']) < 0;
    }

    private static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }

    private sealed record RedeemExternalShareLinkRequest(
        Guid TenantId,
        string AccessToken,
        string GuestEmail);

    private sealed record ExternalShareRedemptionResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        int MaxUses,
        int UsedCount,
        string ReasonCode);

    private sealed record ErrorResponse(string ReasonCode);
}
