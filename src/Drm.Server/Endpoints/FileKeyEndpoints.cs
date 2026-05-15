using Drm.Crypto;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class FileKeyEndpoints
{
    public static IEndpointRouteBuilder MapFileKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/files/{fileId:guid}/keys");

        group.MapPost("/wrap", WrapFileKeyAsync);
        group.MapPost("/unwrap", UnwrapFileKeyAsync);

        return endpoints;
    }

    private static async Task<IResult> WrapFileKeyAsync(
        Guid fileId,
        WrapFileKeyRequest request,
        AppDbContext dbContext,
        IFileKeyProtector fileKeyProtector,
        CancellationToken cancellationToken)
    {
        var fileExists = await dbContext.ProtectedFiles
            .AsNoTracking()
            .AnyAsync(file => file.TenantId == request.TenantId && file.Id == fileId, cancellationToken);
        if (!fileExists)
        {
            return Results.NotFound();
        }

        byte[] fileKey;
        try
        {
            fileKey = Convert.FromBase64String(request.FileKeyBase64);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new ErrorResponse("invalid_file_key"));
        }

        if (fileKey.Length != EnvelopeCrypto.KeySizeBytes)
        {
            return Results.BadRequest(new ErrorResponse("invalid_file_key"));
        }

        var wrapped = fileKeyProtector.Wrap(request.TenantId, fileId, fileKey);
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.FileKeys
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId && candidate.FileId == fileId, cancellationToken);

        var created = existing is null;
        if (existing is null)
        {
            existing = new FileKeyEntity
            {
                TenantId = request.TenantId,
                FileId = fileId,
                CreatedAtUtc = now
            };
            dbContext.FileKeys.Add(existing);
        }

        existing.WrappedKeyNonceBase64 = wrapped.NonceBase64;
        existing.WrappedKeyCiphertextBase64 = wrapped.CiphertextBase64;
        existing.WrappedKeyTagBase64 = wrapped.TagBase64;
        existing.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new WrapFileKeyResponse(request.TenantId, fileId, "wrapped");
        return created
            ? Results.Created($"/api/files/{fileId}/keys/wrap", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> UnwrapFileKeyAsync(
        Guid fileId,
        UnwrapFileKeyRequest request,
        AppDbContext dbContext,
        IFileKeyProtector fileKeyProtector,
        PolicyDecisionService policyDecisionService,
        CancellationToken cancellationToken)
    {
        var decision = await policyDecisionService.DecideAsync(
            request.TenantId,
            fileId,
            request.UserId,
            request.DeviceId,
            request.RequestedPermission,
            cancellationToken);

        if (decision.InvalidPermission)
        {
            return Results.BadRequest(new ErrorResponse("invalid_permissions"));
        }

        if (!decision.FileFound)
        {
            return Results.NotFound();
        }

        if (!decision.Allowed)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var stored = await dbContext.FileKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TenantId == request.TenantId && candidate.FileId == fileId, cancellationToken);
        if (stored is null)
        {
            return Results.NotFound();
        }

        var fileKey = fileKeyProtector.Unwrap(
            request.TenantId,
            fileId,
            new WrappedFileKey(
                stored.WrappedKeyNonceBase64,
                stored.WrappedKeyCiphertextBase64,
                stored.WrappedKeyTagBase64));

        return Results.Ok(new UnwrapFileKeyResponse(
            request.TenantId,
            fileId,
            Convert.ToBase64String(fileKey),
            decision.AllowedPermissions.ToString(),
            decision.WatermarkTemplate,
            decision.OfflineLeaseExpiresAtUtc));
    }

    private sealed record WrapFileKeyRequest(Guid TenantId, string FileKeyBase64);

    private sealed record WrapFileKeyResponse(Guid TenantId, Guid FileId, string Status);

    private sealed record UnwrapFileKeyRequest(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission);

    private sealed record UnwrapFileKeyResponse(
        Guid TenantId,
        Guid FileId,
        string FileKeyBase64,
        string AllowedPermissions,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
