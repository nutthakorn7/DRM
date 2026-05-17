using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public interface IBoxIntegrationService
{
    Task<BoxConnectionResult> TestConnectionAsync(Guid tenantId, CancellationToken cancellationToken);

    bool VerifyWebhookSignature(string webhookSecret, string body, string? signaturePrimary, string? signatureSecondary);
}

public sealed record BoxConnectionResult(bool Success, string Status, string? ErrorMessage);

public sealed class BoxIntegrationService(
    AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    ILogger<BoxIntegrationService> logger) : IBoxIntegrationService
{
    private const string BoxTokenEndpoint = "https://api.box.com/oauth2/token";

    public async Task<BoxConnectionResult> TestConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var config = await dbContext.TenantBoxIntegrationConfigs
            .SingleOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null)
        {
            return new BoxConnectionResult(false, "not_configured", "Box integration is not configured for this tenant.");
        }

        if (string.IsNullOrWhiteSpace(config.ClientId) ||
            string.IsNullOrWhiteSpace(config.ClientSecret) ||
            string.IsNullOrWhiteSpace(config.EnterpriseId))
        {
            return new BoxConnectionResult(false, "missing_credentials", "Client ID, secret, and enterprise ID are required.");
        }

        using var http = httpClientFactory.CreateClient("BoxApi");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["box_subject_type"] = "enterprise",
            ["box_subject_id"] = config.EnterpriseId
        });

        try
        {
            using var response = await http.PostAsync(BoxTokenEndpoint, form, cancellationToken);
            var status = response.IsSuccessStatusCode ? "ok" : $"http_{(int)response.StatusCode}";
            await PersistConnectionStatusAsync(config, status, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Box token request failed: {Status} {Body}", response.StatusCode, body);
                return new BoxConnectionResult(false, status, $"Box returned {(int)response.StatusCode}.");
            }

            return new BoxConnectionResult(true, status, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Box connection test failed");
            await PersistConnectionStatusAsync(config, "error", cancellationToken);
            return new BoxConnectionResult(false, "error", exception.Message);
        }
    }

    public bool VerifyWebhookSignature(string webhookSecret, string body, string? signaturePrimary, string? signatureSecondary)
    {
        if (string.IsNullOrEmpty(webhookSecret) || string.IsNullOrEmpty(body))
        {
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(webhookSecret);
        using var hmac = new HMACSHA256(keyBytes);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var expected = Convert.ToBase64String(hmac.ComputeHash(bodyBytes));

        return SignaturesMatch(expected, signaturePrimary) || SignaturesMatch(expected, signatureSecondary);
    }

    private static bool SignaturesMatch(string expected, string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        return expectedBytes.Length == candidateBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
    }

    private async Task PersistConnectionStatusAsync(
        TenantBoxIntegrationConfigEntity config,
        string status,
        CancellationToken cancellationToken)
    {
        config.LastConnectionStatus = status;
        config.LastConnectionAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
