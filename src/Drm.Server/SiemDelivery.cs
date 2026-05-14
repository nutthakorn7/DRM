using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Drm.Server;

public interface ISiemEventSink
{
    Task SendAsync(SiemWebhookEntity webhook, AuditEventEntity auditEvent, CancellationToken cancellationToken);
}

public sealed class HttpSiemEventSink(HttpClient httpClient) : ISiemEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null
    };

    public async Task SendAsync(SiemWebhookEntity webhook, AuditEventEntity auditEvent, CancellationToken cancellationToken)
    {
        if (!webhook.Enabled || !await SiemWebhookUrlGuard.IsAllowedAsync(webhook.Url, cancellationToken))
        {
            return;
        }

        using var response = await httpClient.PostAsJsonAsync(
            webhook.Url,
            new SiemAuditEventPayload(
                auditEvent.TenantId,
                auditEvent.FileId,
                auditEvent.UserId,
                auditEvent.EventType,
                auditEvent.ReasonCode,
                auditEvent.CreatedAtUtc),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private sealed record SiemAuditEventPayload(
        Guid TenantId,
        Guid? FileId,
        Guid? UserId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);
}

public interface ISiemDispatcher
{
    Task DispatchAsync(AuditEventEntity auditEvent, CancellationToken cancellationToken);
}

public sealed class SiemDispatcher(
    AppDbContext dbContext,
    ISiemEventSink eventSink,
    ILogger<SiemDispatcher> logger) : ISiemDispatcher
{
    private static readonly TimeSpan WebhookTimeout = TimeSpan.FromSeconds(5);

    public async Task DispatchAsync(AuditEventEntity auditEvent, CancellationToken cancellationToken)
    {
        var webhooks = await dbContext.SiemWebhooks
            .AsNoTracking()
            .Where(webhook => webhook.TenantId == auditEvent.TenantId && webhook.Enabled)
            .OrderBy(webhook => webhook.WebhookId)
            .ToListAsync(cancellationToken);

        foreach (var webhook in webhooks)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WebhookTimeout);

            try
            {
                await eventSink.SendAsync(webhook, auditEvent, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "SIEM webhook {WebhookId} timed out for audit event {AuditEventId}.",
                    webhook.WebhookId,
                    auditEvent.Id);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(
                    ex,
                    "SIEM webhook {WebhookId} failed for audit event {AuditEventId}.",
                    webhook.WebhookId,
                    auditEvent.Id);
            }
        }
    }
}
