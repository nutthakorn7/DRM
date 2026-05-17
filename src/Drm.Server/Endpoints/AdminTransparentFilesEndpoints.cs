using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminTransparentFilesEndpoints
{
    public static IEndpointRouteBuilder MapAdminTransparentFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/transparent-files");

        group.MapPost("/", RegisterAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{fileId:guid}", GetAsync);
        group.MapDelete("/{fileId:guid}", DeregisterAsync);

        endpoints.MapGet("/api/admin/transparent-files/secret", GetTrailerSecret);

        return endpoints;
    }

    private static async Task<Results<Created<TransparentFileResponse>, Conflict, BadRequest<ErrorResponse>>> RegisterAsync(
        RegisterRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
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
            EventType = "system_changed",
            ReasonCode = "transparent_file_registered",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/admin/transparent-files/{entity.FileId}?tenantId={entity.TenantId}",
            TransparentFileResponse.From(entity));
    }

    private static async Task<IReadOnlyList<TransparentFileResponse>> ListAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await dbContext.TransparentProtectedFiles
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.OriginalFileName)
            .Select(t => new TransparentFileResponse(
                t.TenantId, t.FileId, t.OwnerUserId, t.OriginalFileName, t.ContentType,
                t.PolicyTemplateId, t.RegisteredAtUtc))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Results<Ok<TransparentFileResponse>, NotFound>> GetAsync(
        Guid fileId,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.TransparentProtectedFiles
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TenantId == tenantId && t.FileId == fileId, cancellationToken);
        return entity is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TransparentFileResponse.From(entity));
    }

    private static async Task<Results<NoContent, NotFound>> DeregisterAsync(
        Guid fileId,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.TransparentProtectedFiles
            .SingleOrDefaultAsync(t => t.TenantId == tenantId && t.FileId == fileId, cancellationToken);
        if (entity is null)
        {
            return TypedResults.NotFound();
        }
        dbContext.TransparentProtectedFiles.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static Ok<TrailerSecretResponse> GetTrailerSecret(IConfiguration configuration)
    {
        var secret = configuration["Drm:Security:TransparentTrailerSecret"]
            ?? "drm-transparent-default-secret";
        return TypedResults.Ok(new TrailerSecretResponse(secret));
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

    private sealed record ErrorResponse(string ReasonCode);
}
