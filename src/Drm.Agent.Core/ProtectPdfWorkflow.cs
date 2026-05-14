using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class ProtectPdfWorkflow(IDrmServerClient serverClient)
{
    private const string PdfContentType = "application/pdf";

    public async Task<byte[]> ProtectAsync(
        TenantId tenantId,
        UserId ownerUserId,
        byte[] pdfBytes,
        byte[] fileKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentNullException.ThrowIfNull(fileKey);

        var fileId = ProtectedFileId.New();
        await serverClient.RegisterFileAsync(
            tenantId.Value,
            fileId.Value,
            ownerUserId.Value,
            PdfContentType,
            DateTimeOffset.UtcNow.AddDays(7),
            Permission.View | Permission.Print,
            cancellationToken);

        using var stream = new MemoryStream();
        ProtectedFileWriter.Write(stream, tenantId, fileId, PdfContentType, fileKey, pdfBytes);
        return stream.ToArray();
    }
}
