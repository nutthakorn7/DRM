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
        var opened = await new OpenProtectedFileWorkflow(serverClient, fileKeyStore, decisionCache)
            .OpenAsync(protectedPath, userId, deviceId, cancellationToken);

        return new OpenedProtectedPdf(
            opened.TenantId,
            opened.FileId,
            opened.Content,
            opened.Watermark,
            opened.Permissions);
    }
}
