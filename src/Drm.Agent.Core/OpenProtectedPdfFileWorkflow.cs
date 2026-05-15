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

        var fileKey = await LoadFileKeyAsync(package, userId, deviceId, cancellationToken);

        return await new OpenProtectedPdfWorkflow(serverClient, decisionCache)
            .OpenAsync(protectedBytes, userId, deviceId, fileKey, cancellationToken);
    }

    private async Task<byte[]> LoadFileKeyAsync(
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
            var localFileKey = await fileKeyStore.LoadAsync(
                package.Header.TenantId,
                package.Header.FileId,
                cancellationToken);

            if (localFileKey is null)
            {
                throw new UnauthorizedAccessException("Access denied: file_key_missing", exception);
            }

            return localFileKey;
        }
    }
}
