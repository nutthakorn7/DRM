using Drm.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class FilesEndpoints
{
    public static IEndpointRouteBuilder MapFilesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/files");

        group.MapPost("/", RegisterFileAsync);
        group.MapPost("/{fileId:guid}/revoke", RevokeFileAsync);

        return endpoints;
    }

    private static async Task<Created<RegisterFileResponse>> RegisterFileAsync(
        RegisterFileRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var file = new ProtectedFileEntity
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            OwnerUserId = request.OwnerUserId,
            ContentType = request.ContentType,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Revoked = false,
            Permissions = request.Permissions,
            WatermarkTemplate = request.WatermarkTemplate
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
            file.Permissions,
            file.WatermarkTemplate));
    }

    private static async Task<Results<Ok<RevokeFileResponse>, NotFound>> RevokeFileAsync(
        Guid fileId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var file = await dbContext.ProtectedFiles
            .SingleOrDefaultAsync(candidate => candidate.Id == fileId, cancellationToken);

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
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        Permission Permissions,
        string WatermarkTemplate);

    private sealed record RegisterFileResponse(
        Guid FileId,
        Guid TenantId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        Permission Permissions,
        string WatermarkTemplate);

    private sealed record RevokeFileResponse(Guid FileId, bool Revoked);
}
