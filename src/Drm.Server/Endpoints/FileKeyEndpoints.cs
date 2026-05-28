using Drm.Crypto;
using Drm.Domain;
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
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // v1.9: IP allowlist enforcement
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var ipDenied = await IpAllowlistService.IsDeniedAsync(dbContext, request.TenantId, clientIp, cancellationToken);
        if (ipDenied)
            return Results.Json(new ErrorResponse("ip_not_allowed"), statusCode: StatusCodes.Status403Forbidden);

        var signatureFailure = await ValidateDeviceSignatureForTrustedTenantAsync(
            request,
            fileId,
            dbContext,
            cancellationToken);
        if (signatureFailure is not null)
        {
            return signatureFailure;
        }

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
            // Surface the reason code in the body so clients can tell
            // "your X opens are used up" apart from "this file was revoked"
            // and show a useful message. The status code stays 403.
            return Results.Json(
                new ErrorResponse(decision.ReasonCode),
                statusCode: StatusCodes.Status403Forbidden);
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
            decision.OfflineLeaseExpiresAtUtc,
            decision.MaxOpens,
            decision.OpensRemaining));
    }

    private sealed record WrapFileKeyRequest(Guid TenantId, string FileKeyBase64);

    private sealed record WrapFileKeyResponse(Guid TenantId, Guid FileId, string Status);

    private sealed record UnwrapFileKeyRequest(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission,
        DeviceRequestSignature? DeviceSignature);

    private sealed record UnwrapFileKeyResponse(
        Guid TenantId,
        Guid FileId,
        string FileKeyBase64,
        string AllowedPermissions,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc,
        int? MaxOpens,
        int? OpensRemaining);

    private sealed record ErrorResponse(string ReasonCode);

    private static async Task<IResult?> ValidateDeviceSignatureForTrustedTenantAsync(
        UnwrapFileKeyRequest request,
        Guid fileId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var trustEnabled = await dbContext.TenantDeviceTrustConfigs
            .AsNoTracking()
            .AnyAsync(config => config.TenantId == request.TenantId && config.Enabled, cancellationToken);
        if (!trustEnabled)
        {
            return null;
        }

        if (request.DeviceId == Guid.Empty)
        {
            return null;
        }

        var device = await dbContext.AgentDevices
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.DeviceId == request.DeviceId,
                cancellationToken);

        if (device is null ||
            string.IsNullOrWhiteSpace(device.DeviceSigningKeyHashBase64) ||
            request.DeviceSignature is null)
        {
            return Results.Json(
                new ErrorResponse("device_signature_required"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (device.UserId != request.UserId)
        {
            return Results.Json(
                new ErrorResponse("device_user_mismatch"),
                statusCode: StatusCodes.Status403Forbidden);
        }

        var payload = DeviceRequestSigning.UnwrapPayload(
            request.TenantId,
            fileId,
            request.UserId,
            request.DeviceId,
            request.RequestedPermission);

        return DeviceRequestSigning.Verify(
            device.DeviceSigningKeyHashBase64,
            payload,
            request.DeviceSignature,
            DateTimeOffset.UtcNow)
            ? null
            : Results.Json(
                new ErrorResponse("device_signature_invalid"),
                statusCode: StatusCodes.Status403Forbidden);
    }
}
