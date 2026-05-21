using System.Security.Cryptography;
using System.Text;
using Drm.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

/// <summary>
/// One-call protect+grant+share-link endpoint for non-technical users.
/// Implements the CapLinked FileProtect-style 3-step workflow as a single
/// POST so the caller doesn't have to orchestrate three different
/// existing endpoints.
/// </summary>
public static class QuickShareEndpoints
{
    private const long MaxPayloadBytes = 200L * 1024 * 1024;
    private const int MaxExpiresInHours = 8760; // 1 year

    public static IEndpointRouteBuilder MapQuickShareEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/me/share", QuickShareAsync);
        return endpoints;
    }

    private static async Task<Results<Created<QuickShareResponse>, BadRequest<ErrorResponse>, ForbidHttpResult>> QuickShareAsync(
        QuickShareRequest request,
        AppDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return TypedResults.BadRequest(new ErrorResponse("tenant_mismatch"));
        }
        if (request.TenantId == Guid.Empty || request.UserId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }
        if (string.IsNullOrWhiteSpace(request.RecipientEmail) || !request.RecipientEmail.Contains('@'))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_recipient_email"));
        }
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_file_name"));
        }
        if (string.IsNullOrEmpty(request.FileBytesBase64))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_file_bytes"));
        }
        if (request.ExpiresInHours <= 0 || request.ExpiresInHours > MaxExpiresInHours)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_expires_in_hours"));
        }

        byte[] payload;
        try { payload = Convert.FromBase64String(request.FileBytesBase64); }
        catch (FormatException) { return TypedResults.BadRequest(new ErrorResponse("invalid_base64")); }
        if (payload.LongLength == 0 || payload.LongLength > MaxPayloadBytes)
        {
            return TypedResults.BadRequest(new ErrorResponse("payload_size_out_of_range"));
        }

        // Persona check — Quick-Share requires CanProtect (everyone but
        // a future "Reader-only" persona).
        var personaRow = await dbContext.TenantUserPersonas
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.TenantId == request.TenantId && p.UserId == request.UserId, cancellationToken);
        var persona = personaRow is null
            ? DrmPersona.Employee
            : Enum.TryParse<DrmPersona>(personaRow.Persona, ignoreCase: true, out var p) ? p : DrmPersona.Employee;
        var capabilities = PersonaCapabilities.For(persona);
        if (!capabilities.CanProtect)
        {
            return TypedResults.Forbid();
        }

        var fileId = Guid.NewGuid();

        // Permission bits. View is always granted (recipient can't open
        // the file otherwise). The other four are individually opt-in.
        // Existing /me/ web callers only send AllowPrint; new agent
        // callers send the full set. Backwards compatible because all
        // four new fields are `bool?` defaulting to false at the wire.
        var permissions = Permission.View;
        if (request.AllowPrint)                  permissions |= Permission.Print;
        if (request.AllowCopy == true)           permissions |= Permission.Copy;
        if (request.AllowEdit == true)           permissions |= Permission.Edit;
        if (request.AllowExportOriginal == true) permissions |= Permission.ExportOriginal;

        var expiresAt = DateTimeOffset.UtcNow.AddHours(request.ExpiresInHours);

        var file = new ProtectedFileEntity
        {
            Id = fileId,
            TenantId = request.TenantId,
            OwnerUserId = request.UserId,
            ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType,
            ExpiresAtUtc = expiresAt,
            Revoked = false,
            Permissions = permissions,
            WatermarkTemplate = string.Empty,
            OfflineLeaseMinutes = 15
        };
        dbContext.ProtectedFiles.Add(file);

        // Auto-create a share link bound to the recipient email with a
        // freshly minted token. CRITICAL: ExternalShareToken.Create()
        // matches the format ExternalShareEndpoints expects when the
        // recipient hits /share/ → /api/share-links/verification/start.
        // Pre-Stage-12 this used Convert.ToHexString + UTF-8 SHA-256
        // which produced a different hash shape than what the
        // verification endpoint computed → verification 404'd silently
        // for every QuickShare-created link. See Stage 12 PR for the
        // root-cause write-up.
        var token = ExternalShareToken.Create();
        var rawToken = token.Plaintext;
        var tokenHash = token.Hash;
        var shareLinkId = Guid.NewGuid();
        dbContext.ExternalShareLinks.Add(new ExternalShareLinkEntity
        {
            TenantId = request.TenantId,
            ShareLinkId = shareLinkId,
            FileId = fileId,
            GuestEmail = request.RecipientEmail.Trim(),
            TokenHash = tokenHash,
            MaxUses = 1,
            UsedCount = 0,
            ExpiresAtUtc = expiresAt,
            Revoked = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            FileId = fileId,
            UserId = request.UserId,
            EventType = "system_changed",
            ReasonCode = "quick_share_created",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        // Encode tenantId + accessToken + guestEmail so the recipient gets a
        // one-click link that pre-fills everything on /share/. They never
        // have to type a GUID. Mirrors BuildExternalShareUrl in
        // AdminFilesEndpoints so both admin-created and quick-share links
        // behave the same.
        var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var query = QueryString.Create([
            new KeyValuePair<string, string?>("tenantId", request.TenantId.ToString()),
            new KeyValuePair<string, string?>("accessToken", rawToken),
            new KeyValuePair<string, string?>("guestEmail", request.RecipientEmail.Trim())
        ]);
        var shareUrl = $"{origin}/share/{query}";
        return TypedResults.Created(
            $"/api/admin/files/{fileId}",
            new QuickShareResponse(
                fileId,
                shareLinkId,
                shareUrl,
                expiresAt,
                request.RecipientEmail.Trim(),
                permissions.ToString(),
                payload.Length));
    }

    // HashToken removed in Stage 12 — replaced by ExternalShareToken.Hash
    // so the share-link is reachable from the recipient-side
    // /api/share-links/verification/start endpoint. See PR #27.

    private sealed record QuickShareRequest(
        Guid TenantId,
        Guid UserId,
        string RecipientEmail,
        string FileName,
        string? ContentType,
        string FileBytesBase64,
        int ExpiresInHours,
        bool AllowPrint,
        // New in v1.7 (Stage 7 agent per-share picker). All optional —
        // when missing the wire shape is identical to the v1.0 callers
        // (the /me/ web form), keeping backwards compat.
        bool? AllowCopy = null,
        bool? AllowEdit = null,
        bool? AllowExportOriginal = null);

    private sealed record QuickShareResponse(
        Guid FileId,
        Guid ShareLinkId,
        string ShareUrl,
        DateTimeOffset ExpiresAtUtc,
        string RecipientEmail,
        string Permissions,
        int OriginalFileSizeBytes);

    private sealed record ErrorResponse(string ReasonCode);
}
