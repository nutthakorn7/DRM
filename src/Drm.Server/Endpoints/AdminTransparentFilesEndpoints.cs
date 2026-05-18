using System.Text;
using Drm.Crypto;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminTransparentFilesEndpoints
{
    private const long MaxStampPayloadBytes = 200L * 1024 * 1024;

    public static IEndpointRouteBuilder MapAdminTransparentFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/transparent-files");

        group.MapPost("/", RegisterAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{fileId:guid}", GetAsync);
        group.MapDelete("/{fileId:guid}", DeregisterAsync);
        group.MapPost("/stamp", StampAsync);
        group.MapPost("/verify", VerifyAsync);
        group.MapGet("/secret", GetTrailerSecret);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty || request.FileId == Guid.Empty || request.OwnerUserId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }

        if (string.IsNullOrWhiteSpace(request.OriginalFileName))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_file_name"));
        }

        var exists = await dbContext.TransparentProtectedFiles
            .AsNoTracking()
            .AnyAsync(t => t.TenantId == request.TenantId && t.FileId == request.FileId, cancellationToken);
        if (exists)
        {
            return TypedResults.Conflict();
        }

        var entity = new TransparentProtectedFileEntity
        {
            TenantId = request.TenantId,
            FileId = request.FileId,
            OwnerUserId = request.OwnerUserId,
            OriginalFileName = request.OriginalFileName.Trim(),
            ContentType = (request.ContentType ?? string.Empty).Trim(),
            PolicyTemplateId = request.PolicyTemplateId,
            RegisteredAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TransparentProtectedFiles.Add(entity);
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            FileId = request.FileId,
            ActorAdminId = AdminAudit.ActorId(httpContext),
            EventType = "system_changed",
            ReasonCode = "transparent_file_registered",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/admin/transparent-files/{entity.FileId}?tenantId={entity.TenantId}",
            TransparentFileResponse.From(entity));
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsRead, tenantId, out var fail))
            return fail!;
        var items = await dbContext.TransparentProtectedFiles
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.OriginalFileName)
            .Select(t => new TransparentFileResponse(
                t.TenantId, t.FileId, t.OwnerUserId, t.OriginalFileName, t.ContentType,
                t.PolicyTemplateId, t.RegisteredAtUtc))
            .ToListAsync(cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetAsync(
        Guid fileId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsRead, tenantId, out var fail))
            return fail!;
        var entity = await dbContext.TransparentProtectedFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenantId == tenantId && t.FileId == fileId, cancellationToken);
        return entity is null
            ? Results.NotFound()
            : Results.Ok(TransparentFileResponse.From(entity));
    }

    private static async Task<IResult> DeregisterAsync(
        Guid fileId,
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsWrite, tenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(tenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        var entity = await dbContext.TransparentProtectedFiles
            .SingleOrDefaultAsync(t => t.TenantId == tenantId && t.FileId == fileId, cancellationToken);
        if (entity is null)
        {
            return Results.NotFound();
        }
        dbContext.TransparentProtectedFiles.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static IResult VerifyAsync(
        VerifyRequest request,
        HttpContext httpContext,
        IConfiguration configuration)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (string.IsNullOrEmpty(request.FileBytesBase64))
        {
            return Results.BadRequest(new ErrorResponse("invalid_file_bytes"));
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.FileBytesBase64);
        }
        catch (FormatException)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_base64"));
        }
        if (bytes.LongLength == 0 || bytes.LongLength > MaxStampPayloadBytes)
        {
            return TypedResults.BadRequest(new ErrorResponse("payload_size_out_of_range"));
        }

        var secret = configuration["Drm:Security:TransparentTrailerSecret"];
        if (string.IsNullOrWhiteSpace(secret) ||
            string.Equals(secret, SecurityStartupGuard.DefaultTrailerSecretPlaceholder, StringComparison.Ordinal))
        {
            return TypedResults.BadRequest(new ErrorResponse("trailer_secret_not_configured"));
        }

        var hmacKey = Encoding.UTF8.GetBytes(secret);
        if (TransparentEnvelope.TryReadTrailer(bytes, hmacKey, out var metadata, out var originalLength))
        {
            return Results.Ok(new VerifyResponse(
                Valid: true,
                metadata!.TenantId,
                metadata.FileId,
                metadata.OwnerUserId,
                metadata.ContentType,
                metadata.OriginalFileName,
                metadata.RegisteredAtUtc,
                metadata.PolicyTemplateId,
                OriginalLength: originalLength));
        }
        return Results.Ok(new VerifyResponse(false, null, null, null, null, null, null, null, null));
    }

    private static IResult GetTrailerSecret(
        HttpContext httpContext,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        // The trailer secret is the only authenticity anchor for transparent
        // files. Returning it over the network is gated to specific deployments
        // (folder-watcher service inside the trust boundary). All other
        // callers should use POST /stamp instead, which performs the stamping
        // server-side and never reveals the secret.
        var distributionAllowed = configuration.GetValue("Drm:Security:AllowTrailerSecretDistribution", false);
        if (!distributionAllowed)
        {
            return Results.NotFound();
        }

        var secret = configuration["Drm:Security:TransparentTrailerSecret"];
        if (string.IsNullOrWhiteSpace(secret) ||
            string.Equals(secret, SecurityStartupGuard.DefaultTrailerSecretPlaceholder, StringComparison.Ordinal))
        {
            return Results.BadRequest(new ErrorResponse("trailer_secret_not_configured"));
        }
        return Results.Ok(new TrailerSecretResponse(secret));
    }

    private static IResult StampAsync(
        StampRequest request,
        HttpContext httpContext,
        IConfiguration configuration)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.SettingsWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
        {
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        }

        if (request.TenantId == Guid.Empty || request.OwnerUserId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }
        if (string.IsNullOrWhiteSpace(request.OriginalFileName))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_file_name"));
        }
        if (string.IsNullOrEmpty(request.FileBytesBase64))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_file_bytes"));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.FileBytesBase64);
        }
        catch (FormatException)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_base64"));
        }
        if (bytes.LongLength == 0 || bytes.LongLength > MaxStampPayloadBytes)
        {
            return TypedResults.BadRequest(new ErrorResponse("payload_size_out_of_range"));
        }

        var secret = configuration["Drm:Security:TransparentTrailerSecret"];
        if (string.IsNullOrWhiteSpace(secret) ||
            string.Equals(secret, SecurityStartupGuard.DefaultTrailerSecretPlaceholder, StringComparison.Ordinal))
        {
            return TypedResults.BadRequest(new ErrorResponse("trailer_secret_not_configured"));
        }
        var hmacKey = Encoding.UTF8.GetBytes(secret);

        var fileId = request.FileId == Guid.Empty ? Guid.NewGuid() : request.FileId;
        var metadata = new TransparentMetadata(
            request.TenantId,
            fileId,
            request.OwnerUserId,
            request.ContentType ?? "application/octet-stream",
            request.OriginalFileName.Trim(),
            DateTimeOffset.UtcNow,
            request.PolicyTemplateId);

        var stamped = TransparentEnvelope.AppendTrailer(bytes, metadata, hmacKey);
        return Results.Ok(new StampResponse(
            fileId,
            Convert.ToBase64String(stamped),
            stamped.Length,
            stamped.Length - bytes.Length));
    }

    private sealed record RegisterRequest(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string OriginalFileName,
        string? ContentType,
        Guid? PolicyTemplateId);

    private sealed record TransparentFileResponse(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string OriginalFileName,
        string ContentType,
        Guid? PolicyTemplateId,
        DateTimeOffset RegisteredAtUtc)
    {
        public static TransparentFileResponse From(TransparentProtectedFileEntity e)
            => new(e.TenantId, e.FileId, e.OwnerUserId, e.OriginalFileName, e.ContentType,
                   e.PolicyTemplateId, e.RegisteredAtUtc);
    }

    private sealed record TrailerSecretResponse(string Secret);

    private sealed record StampRequest(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string OriginalFileName,
        string? ContentType,
        Guid? PolicyTemplateId,
        string FileBytesBase64);

    private sealed record StampResponse(
        Guid FileId,
        string StampedFileBytesBase64,
        long StampedSizeBytes,
        long TrailerSizeBytes);

    private sealed record VerifyRequest(string FileBytesBase64);

    private sealed record VerifyResponse(
        bool Valid,
        Guid? TenantId,
        Guid? FileId,
        Guid? OwnerUserId,
        string? ContentType,
        string? OriginalFileName,
        DateTimeOffset? RegisteredAtUtc,
        Guid? PolicyTemplateId,
        int? OriginalLength);

    private sealed record ErrorResponse(string ReasonCode);
}
