using Drm.Container;
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
        ArgumentNullException.ThrowIfNull(protectedBytes);
        ArgumentNullException.ThrowIfNull(fileKey);

        using var stream = new MemoryStream(protectedBytes, writable: false);
        var package = ProtectedFileReader.Read(stream);
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

    internal static OpenedProtectedPdf OpenWithDecision(
        ProtectedFilePackage package,
        byte[] fileKey,
        Guid userId,
        string? watermarkTemplate,
        Permission allowedPermissions)
    {
        return new OpenedProtectedPdf(
            package.Decrypt(fileKey),
            ApplyWatermark(watermarkTemplate, userId, package.Header.FileId),
            allowedPermissions);
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
