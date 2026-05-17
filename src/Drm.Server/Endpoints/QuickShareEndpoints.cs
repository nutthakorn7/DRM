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
        var permissions = request.AllowPrint
            ? Permission.View | Permission.Print
            : Permission.View;
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
        // freshly minted token.
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var tokenHash = HashToken(rawToken);
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

        var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var shareUrl = $"{origin}/share/?token={rawToken}";
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

    private static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private sealed record QuickShareRequest(
        Guid TenantId,
        Guid UserId,
        string RecipientEmail,
        string FileName,
        string? ContentType,
        string FileBytesBase64,
        int ExpiresInHours,
        bool AllowPrint);

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
