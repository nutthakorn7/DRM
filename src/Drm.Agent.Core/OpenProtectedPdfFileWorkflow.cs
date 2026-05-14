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

        var fileKey = await fileKeyStore.LoadAsync(
            package.Header.TenantId,
            package.Header.FileId,
            cancellationToken);

        if (fileKey is null)
        {
            throw new UnauthorizedAccessException("Access denied: file_key_missing");
        }

        return await new OpenProtectedPdfWorkflow(serverClient, decisionCache)
            .OpenAsync(protectedBytes, userId, deviceId, fileKey, cancellationToken);
    }
}
