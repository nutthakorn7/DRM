using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class OpenProtectedPdfWorkflow(IDrmServerClient serverClient, IPolicyDecisionCache? decisionCache = null)
{
    public async Task<OpenedProtectedPdf> OpenAsync(
        byte[] protectedBytes,
        UserId userId,
        DeviceId deviceId,
        byte[] fileKey,
        CancellationToken cancellationToken)
    {
        var opened = await new OpenProtectedFileWorkflow(serverClient, decisionCache: decisionCache)
            .OpenAsync(protectedBytes, userId, deviceId, fileKey, cancellationToken);

        return new OpenedProtectedPdf(
            opened.TenantId,
            opened.FileId,
            opened.Content,
            opened.Watermark,
            opened.Permissions);
    }
}

public sealed record OpenedProtectedPdf(Guid TenantId, Guid FileId, byte[] Content, string Watermark, Permission Permissions);
