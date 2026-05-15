using System.Net;
using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class OpenProtectedFileWorkflow(
    IDrmServerClient serverClient,
    IFileKeyStore? fileKeyStore = null,
    IPolicyDecisionCache? decisionCache = null)
{
    public async Task<OpenedProtectedFile> OpenAsync(
        byte[] protectedBytes,
        UserId userId,
        DeviceId deviceId,
        byte[] fileKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);
        ArgumentNullException.ThrowIfNull(fileKey);

        var package = ReadPackage(protectedBytes);
        var cacheKey = new PolicyDecisionCacheKey(
            package.Header.TenantId,
            package.Header.FileId,
            userId.Value,
            deviceId.Value,
            Permission.View);

        OpenDecision decision;
        try
        {
            decision = await serverClient.DecideAsync(
                package.Header.TenantId,
                package.Header.FileId,
                userId.Value,
                deviceId.Value,
                Permission.View,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            var cached = decisionCache is null
                ? null
                : await decisionCache.TryGetAllowedAsync(cacheKey, DateTimeOffset.UtcNow, cancellationToken);

            if (cached is null)
            {
                throw new UnauthorizedAccessException("Access denied: offline_lease_missing");
            }

            return OpenWithDecision(package, fileKey, userId.Value, cached.WatermarkTemplate, cached.AllowedPermissions);
        }

        if (!decision.Allowed)
        {
            throw new UnauthorizedAccessException($"Access denied: {decision.ReasonCode}");
        }

        if (decisionCache is not null && decision.OfflineLeaseExpiresAtUtc is not null)
        {
            await decisionCache.StoreAsync(
                new CachedPolicyDecision(
                    cacheKey,
                    decision.WatermarkTemplate,
                    decision.AllowedPermissions,
                    decision.OfflineLeaseExpiresAtUtc.Value),
                cancellationToken);
        }

        return OpenWithDecision(package, fileKey, userId.Value, decision.WatermarkTemplate, decision.AllowedPermissions);
    }

    public async Task<OpenedProtectedFile> OpenAsync(
        string protectedPath,
        UserId userId,
        DeviceId deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPath);

        if (!File.Exists(protectedPath))
        {
            throw new FileNotFoundException("Protected file was not found.", protectedPath);
        }

        var protectedBytes = await File.ReadAllBytesAsync(protectedPath, cancellationToken);
        var package = ReadPackage(protectedBytes);

        var unwrapped = await TryUnwrapFileKeyAsync(package, userId, deviceId, cancellationToken);
        if (unwrapped is not null)
        {
            await StoreDecisionAsync(package, userId, deviceId, unwrapped, cancellationToken);
            return OpenWithDecision(
                package,
                unwrapped.FileKey,
                userId.Value,
                unwrapped.WatermarkTemplate,
                unwrapped.AllowedPermissions);
        }

        var localFileKey = await LoadLocalFileKeyAsync(package, cancellationToken);
        return await OpenAsync(protectedBytes, userId, deviceId, localFileKey, cancellationToken);
    }

    internal static OpenedProtectedFile OpenWithDecision(
        ProtectedFilePackage package,
        byte[] fileKey,
        Guid userId,
        string? watermarkTemplate,
        Permission allowedPermissions)
    {
        return new OpenedProtectedFile(
            package.Header.TenantId,
            package.Header.FileId,
            package.Header.ContentType,
            package.Decrypt(fileKey),
            ApplyWatermark(watermarkTemplate, userId, package.Header.FileId),
            allowedPermissions);
    }

    private static ProtectedFilePackage ReadPackage(byte[] protectedBytes)
    {
        using var stream = new MemoryStream(protectedBytes, writable: false);
        return ProtectedFileReader.Read(stream);
    }

    private async Task<UnwrappedFileKey?> TryUnwrapFileKeyAsync(
        ProtectedFilePackage package,
        UserId userId,
        DeviceId deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await serverClient.UnwrapFileKeyAsync(
                package.Header.TenantId,
                package.Header.FileId,
                userId.Value,
                deviceId.Value,
                Permission.View.ToString(),
                cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException("Access denied: file_key_denied", exception);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UnauthorizedAccessException("Access denied: file_key_missing", exception);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null)
        {
            return null;
        }
    }

    private async Task<byte[]> LoadLocalFileKeyAsync(
        ProtectedFilePackage package,
        CancellationToken cancellationToken)
    {
        if (fileKeyStore is null)
        {
            throw new UnauthorizedAccessException("Access denied: file_key_missing");
        }

        var localFileKey = await fileKeyStore.LoadAsync(
            package.Header.TenantId,
            package.Header.FileId,
            cancellationToken);

        if (localFileKey is null)
        {
            throw new UnauthorizedAccessException("Access denied: file_key_missing");
        }

        return localFileKey;
    }

    private async Task StoreDecisionAsync(
        ProtectedFilePackage package,
        UserId userId,
        DeviceId deviceId,
        UnwrappedFileKey unwrapped,
        CancellationToken cancellationToken)
    {
        if (decisionCache is null || unwrapped.OfflineLeaseExpiresAtUtc is null)
        {
            return;
        }

        await decisionCache.StoreAsync(
            new CachedPolicyDecision(
                new PolicyDecisionCacheKey(
                    package.Header.TenantId,
                    package.Header.FileId,
                    userId.Value,
                    deviceId.Value,
                    Permission.View),
                unwrapped.WatermarkTemplate,
                unwrapped.AllowedPermissions,
                unwrapped.OfflineLeaseExpiresAtUtc.Value),
            cancellationToken);
    }

    private static string ApplyWatermark(string? template, Guid userId, Guid fileId)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return template
            .Replace("{user}", userId.ToString("N"), StringComparison.Ordinal)
            .Replace("{file}", fileId.ToString("N"), StringComparison.Ordinal)
            .Replace("{time}", DateTimeOffset.UtcNow.ToString("O"), StringComparison.Ordinal);
    }
}

public sealed record OpenedProtectedFile(
    Guid TenantId,
    Guid FileId,
    string ContentType,
    byte[] Content,
    string Watermark,
    Permission Permissions);
