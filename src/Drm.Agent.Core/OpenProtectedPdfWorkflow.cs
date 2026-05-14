using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class OpenProtectedPdfWorkflow(IDrmServerClient serverClient)
{
    public async Task<OpenedProtectedPdf> OpenAsync(
        byte[] protectedBytes,
        UserId userId,
        DeviceId deviceId,
        byte[] fileKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);
        ArgumentNullException.ThrowIfNull(fileKey);

        using var stream = new MemoryStream(protectedBytes, writable: false);
        var package = ProtectedFileReader.Read(stream);

        var decision = await serverClient.DecideAsync(
            package.Header.TenantId,
            package.Header.FileId,
            userId.Value,
            deviceId.Value,
            Permission.View,
            cancellationToken);

        if (!decision.Allowed)
        {
            throw new UnauthorizedAccessException($"Access denied: {decision.ReasonCode}");
        }

        return new OpenedProtectedPdf(
            package.Decrypt(fileKey),
            ApplyWatermark(decision.WatermarkTemplate, userId.Value, package.Header.FileId),
            decision.AllowedPermissions);
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

public sealed record OpenedProtectedPdf(byte[] Content, string Watermark, Permission Permissions);
