using System.Net;
using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class OpenProtectedPdfFileWorkflow(
    IDrmServerClient serverClient,
    IFileKeyStore fileKeyStore,
    IPolicyDecisionCache? decisionCache = null)
{
    public async Task<OpenedProtectedPdf> OpenAsync(
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
        ProtectedFilePackage package;
        using (var stream = new MemoryStream(protectedBytes, writable: false))
        {
            package = ProtectedFileReader.Read(stream);
        }

        var unwrapped = await TryUnwrapFileKeyAsync(package, userId, deviceId, cancellationToken);
        if (unwrapped is not null)
        {
            await StoreDecisionAsync(package, userId, deviceId, unwrapped, cancellationToken);
            return OpenProtectedPdfWorkflow.OpenWithDecision(
                package,
                unwrapped.FileKey,
                userId.Value,
                unwrapped.WatermarkTemplate,
                unwrapped.AllowedPermissions);
        }

        var fileKey = await LoadLocalFileKeyAsync(package, cancellationToken);

        return await new OpenProtectedPdfWorkflow(serverClient, decisionCache)
            .OpenAsync(protectedBytes, userId, deviceId, fileKey, cancellationToken);
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
}
