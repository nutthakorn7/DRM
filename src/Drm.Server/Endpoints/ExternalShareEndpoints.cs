using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class ExternalShareEndpoints
{
    public static IEndpointRouteBuilder MapExternalShareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/share-links");

        group.MapPost("/redeem", RedeemShareLinkAsync);
        group.MapPost("/verification/start", StartVerificationAsync);
        group.MapPost("/verification/confirm", ConfirmVerificationAsync);
        group.MapPost("/viewer/session", OpenViewerSessionAsync);

        return endpoints;
    }

    private const int VerificationLifetimeMinutes = 10;
    private const int VerificationSessionLifetimeMinutes = 30;
    private const int VerificationMaxAttempts = 5;

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

    private static async Task<Results<Ok<ExternalShareVerificationStartResponse>, BadRequest<ErrorResponse>, NotFound>> StartVerificationAsync(
        StartExternalShareVerificationRequest request,
        AppDbContext dbContext,
        IExternalShareVerificationSender verificationSender,
        CancellationToken cancellationToken)
    {
        var requestError = ValidatePublicShareRequest(request.TenantId, request.AccessToken, request.GuestEmail);
        if (requestError is not null)
        {
            return TypedResults.BadRequest(requestError);
        }

        var guestEmail = NormalizeEmail(request.GuestEmail);
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

        var verificationId = Guid.NewGuid();
        var code = ExternalShareVerificationCode.Generate();
        var expiresAtUtc = now.AddMinutes(VerificationLifetimeMinutes);
        var verification = new ExternalShareVerificationEntity
        {
            TenantId = shareLink.TenantId,
            VerificationId = verificationId,
            ShareLinkId = shareLink.ShareLinkId,
            GuestEmail = guestEmail,
            CodeHash = ExternalShareVerificationCode.Hash(verificationId, code),
            AttemptCount = 0,
            MaxAttempts = VerificationMaxAttempts,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now
        };

        dbContext.ExternalShareVerifications.Add(verification);
        dbContext.AuditEvents.Add(ExternalShareVerificationAuditEvent(
            shareLink.TenantId,
            shareLink.FileId,
            "verification_code_sent",
            now));
        await dbContext.SaveChangesAsync(cancellationToken);

        await verificationSender.SendAsync(
            new ExternalShareVerificationMessage(
                shareLink.TenantId,
                shareLink.ShareLinkId,
                verificationId,
                guestEmail,
                code,
                expiresAtUtc),
            cancellationToken);

        return TypedResults.Ok(new ExternalShareVerificationStartResponse(
            shareLink.TenantId,
            shareLink.ShareLinkId,
            verificationId,
            guestEmail,
            expiresAtUtc,
            "verification_code_sent"));
    }

    private static async Task<Results<Ok<ExternalShareVerificationConfirmResponse>, BadRequest<ErrorResponse>, NotFound>> ConfirmVerificationAsync(
        ConfirmExternalShareVerificationRequest request,
        AppDbContext dbContext,
        BruteForceProtectionService bruteForce,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (request.VerificationId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_verification_id"));
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_verification_code"));
        }

        var verification = await dbContext.ExternalShareVerifications
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.VerificationId == request.VerificationId,
                cancellationToken);

        if (verification is null)
        {
            return TypedResults.NotFound();
        }

        // Tracked load so the brute-force service can mutate Revoked + RevocationReason
        // inline when the threshold is reached. The previous AsNoTracking() was a
        // safe default for a pure read, but ConfirmVerification now needs to be able
        // to revoke this share link as a side-effect of a wrong code.
        var shareLink = await dbContext.ExternalShareLinks
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == verification.TenantId && candidate.ShareLinkId == verification.ShareLinkId,
                cancellationToken);

        if (shareLink is null)
        {
            return TypedResults.NotFound();
        }

        var file = await dbContext.ProtectedFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == shareLink.TenantId && candidate.Id == shareLink.FileId,
                cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        if (verification.VerifiedAtUtc is not null)
        {
            return TypedResults.BadRequest(new ErrorResponse("verification_already_confirmed"));
        }

        if (verification.ExpiresAtUtc <= now)
        {
            return TypedResults.BadRequest(new ErrorResponse("verification_expired"));
        }

        if (verification.AttemptCount >= verification.MaxAttempts)
        {
            return TypedResults.BadRequest(new ErrorResponse("verification_attempts_exceeded"));
        }

        var shareStateError = ValidateRedeemable(shareLink, file, now);
        if (shareStateError is not null)
        {
            return TypedResults.BadRequest(shareStateError);
        }

        if (!ExternalShareVerificationCode.Matches(verification.VerificationId, request.Code, verification.CodeHash))
        {
            verification.AttemptCount += 1;

            // Log this against the share link's running failure tally. If the
            // tenant's brute-force threshold is reached within the window,
            // the service revokes the share link in-place (we then return
            // the same invalid_code error — the caller learns the link is
            // dead on their next attempt, no information leak from a
            // different error code on the breaking attempt).
            var autoRevoked = await bruteForce.RecordFailedAttemptAsync(
                shareLink,
                verification.GuestEmail,
                httpContext.Connection.RemoteIpAddress?.ToString(),
                "invalid_verification_code",
                now,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);

            if (autoRevoked)
            {
                // Surface a distinct error so a legitimate user who hits the
                // wall (e.g. typo storm) knows the link is now unusable
                // rather than retrying forever. Attackers see the same code.
                return TypedResults.BadRequest(new ErrorResponse("share_link_auto_revoked"));
            }

            return TypedResults.BadRequest(new ErrorResponse("invalid_verification_code"));
        }

        var sessionToken = ExternalShareToken.Create();
        var sessionExpiresAtUtc = now.AddMinutes(VerificationSessionLifetimeMinutes);
        if (shareLink.ExpiresAtUtc < sessionExpiresAtUtc)
        {
            sessionExpiresAtUtc = shareLink.ExpiresAtUtc;
        }

        verification.VerifiedAtUtc = now;
        verification.SessionTokenHash = sessionToken.Hash;
        verification.SessionExpiresAtUtc = sessionExpiresAtUtc;
        dbContext.AuditEvents.Add(ExternalShareVerificationAuditEvent(
            shareLink.TenantId,
            shareLink.FileId,
            "verification_confirmed",
            now));

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ExternalShareVerificationConfirmResponse(
            verification.TenantId,
            verification.ShareLinkId,
            verification.VerificationId,
            verification.GuestEmail,
            sessionExpiresAtUtc,
            sessionToken.Plaintext,
            "verification_confirmed"));
    }

    private static async Task<Results<Ok<ExternalShareViewerSessionResponse>, BadRequest<ErrorResponse>, NotFound>> OpenViewerSessionAsync(
        OpenExternalShareViewerSessionRequest request,
        AppDbContext dbContext,
        IAdminNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_tenant_id"));
        }

        if (string.IsNullOrWhiteSpace(request.VerificationSessionToken))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_verification_session_token"));
        }

        var sessionTokenHash = ExternalShareToken.Hash(request.VerificationSessionToken.Trim());
        var verification = await dbContext.ExternalShareVerifications
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId && candidate.SessionTokenHash == sessionTokenHash,
                cancellationToken);

        if (verification is null || verification.VerifiedAtUtc is null || verification.SessionExpiresAtUtc is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        if (verification.SessionExpiresAtUtc <= now)
        {
            return TypedResults.BadRequest(new ErrorResponse("verification_session_expired"));
        }

        var shareLink = await dbContext.ExternalShareLinks
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == verification.TenantId && candidate.ShareLinkId == verification.ShareLinkId,
                cancellationToken);

        if (shareLink is null)
        {
            return TypedResults.NotFound();
        }

        var file = await dbContext.ProtectedFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == shareLink.TenantId && candidate.Id == shareLink.FileId,
                cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        var stateError = ValidateViewerSessionState(verification, shareLink, file, now);
        if (stateError is not null)
        {
            return TypedResults.BadRequest(stateError);
        }

        if (verification.ViewerOpenedAtUtc is null)
        {
            verification.ViewerOpenedAtUtc = now;
            shareLink.UsedCount += 1;
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                TenantId = shareLink.TenantId,
                FileId = shareLink.FileId,
                UserId = null,
                EventType = "external_share_viewer",
                ReasonCode = "external_share_viewer_opened",
                CreatedAtUtc = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await notificationService.NotifyAsync(
                shareLink.TenantId,
                new AdminNotificationEvent(
                    "external_share_viewer_opened",
                    shareLink.FileId,
                    null,
                    shareLink.GuestEmail,
                    now),
                cancellationToken);
        }

        return TypedResults.Ok(new ExternalShareViewerSessionResponse(
            shareLink.TenantId,
            shareLink.ShareLinkId,
            shareLink.FileId,
            shareLink.GuestEmail,
            file.ContentType,
            file.ExpiresAtUtc,
            shareLink.ExpiresAtUtc,
            verification.SessionExpiresAtUtc.Value,
            file.WatermarkTemplate,
            DownloadDisabled: true,
            PrintDisabled: true,
            ExportDisabled: true,
            "viewer_session_ready"));
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

    private static ErrorResponse? ValidateViewerSessionState(
        ExternalShareVerificationEntity verification,
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

        if (verification.ViewerOpenedAtUtc is null && shareLink.UsedCount >= shareLink.MaxUses)
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

    private static ErrorResponse? ValidatePublicShareRequest(
        Guid tenantId,
        string accessToken,
        string guestEmail)
    {
        if (tenantId == Guid.Empty)
        {
            return new ErrorResponse("invalid_tenant_id");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new ErrorResponse("invalid_access_token");
        }

        if (!IsValidGuestEmail(NormalizeEmail(guestEmail)))
        {
            return new ErrorResponse("invalid_guest_email");
        }

        return null;
    }

    private static AuditEventEntity ExternalShareVerificationAuditEvent(
        Guid tenantId,
        Guid fileId,
        string reasonCode,
        DateTimeOffset createdAtUtc)
    {
        return new AuditEventEntity
        {
            TenantId = tenantId,
            FileId = fileId,
            UserId = null,
            EventType = "external_share_verification",
            ReasonCode = reasonCode,
            CreatedAtUtc = createdAtUtc
        };
    }

    private sealed record RedeemExternalShareLinkRequest(
        Guid TenantId,
        string AccessToken,
        string GuestEmail);

    private sealed record StartExternalShareVerificationRequest(
        Guid TenantId,
        string AccessToken,
        string GuestEmail);

    private sealed record ConfirmExternalShareVerificationRequest(
        Guid TenantId,
        Guid VerificationId,
        string Code);

    private sealed record OpenExternalShareViewerSessionRequest(
        Guid TenantId,
        string VerificationSessionToken);

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

    private sealed record ExternalShareVerificationStartResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid VerificationId,
        string GuestEmail,
        DateTimeOffset ExpiresAtUtc,
        string ReasonCode);

    private sealed record ExternalShareVerificationConfirmResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid VerificationId,
        string GuestEmail,
        DateTimeOffset SessionExpiresAtUtc,
        string VerificationSessionToken,
        string ReasonCode);

    private sealed record ExternalShareViewerSessionResponse(
        Guid TenantId,
        Guid ShareLinkId,
        Guid FileId,
        string GuestEmail,
        string ContentType,
        DateTimeOffset FileExpiresAtUtc,
        DateTimeOffset ShareLinkExpiresAtUtc,
        DateTimeOffset SessionExpiresAtUtc,
        string WatermarkTemplate,
        bool DownloadDisabled,
        bool PrintDisabled,
        bool ExportDisabled,
        string ReasonCode);

    private sealed record ErrorResponse(string ReasonCode);
}
