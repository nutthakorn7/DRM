using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class FilesEndpoints
{
    private const string DefaultWatermarkTemplate = "{user} {time} {file}";

    public static IEndpointRouteBuilder MapFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/files");

        group.MapPost("/", RegisterFileAsync);
        group.MapPost("/{fileId:guid}/revoke", RevokeFileAsync);

        return endpoints;
    }

    private static async Task<Results<Created<RegisterFileResponse>, Conflict, BadRequest<ErrorResponse>>> RegisterFileAsync(
        RegisterFileRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!PermissionParser.TryParse(request.Permissions, out var permissions))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_permissions"));
        }

        var duplicateExists = await dbContext.ProtectedFiles
            .AnyAsync(candidate => candidate.TenantId == request.TenantId && candidate.Id == request.FileId, cancellationToken);
        if (duplicateExists)
        {
            return TypedResults.Conflict();
        }

        var file = new ProtectedFileEntity
        {
            Id = request.FileId,
            TenantId = request.TenantId,
            OwnerUserId = request.OwnerUserId,
            ContentType = request.ContentType,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Revoked = false,
            Permissions = permissions,
            WatermarkTemplate = request.WatermarkTemplate ?? DefaultWatermarkTemplate
        };

        dbContext.ProtectedFiles.Add(file);
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            UserId = file.OwnerUserId,
            EventType = "file_registered",
            ReasonCode = "registered",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/files/{file.Id}", new RegisterFileResponse(
            file.Id,
            file.TenantId,
            file.OwnerUserId,
            file.ContentType,
            file.ExpiresAtUtc,
            file.Permissions.ToString(),
            file.WatermarkTemplate));
    }

    private static async Task<Results<Ok<RevokeFileResponse>, NotFound>> RevokeFileAsync(
        Guid fileId,
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == fileId, cancellationToken);

        if (file is null)
        {
            return TypedResults.NotFound();
        }

        file.Revoked = true;
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = file.TenantId,
            FileId = file.Id,
            UserId = file.OwnerUserId,
            EventType = "file_revoked",
            ReasonCode = "revoked",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new RevokeFileResponse(file.Id, file.Revoked));
    }

    private sealed record RegisterFileRequest(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string? WatermarkTemplate);

    private sealed record RegisterFileResponse(
        Guid FileId,
        Guid TenantId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string WatermarkTemplate);

    private sealed record RevokeFileResponse(Guid FileId, bool Revoked);

    private sealed record ErrorResponse(string ReasonCode);
}
