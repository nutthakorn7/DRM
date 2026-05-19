using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

public static class BillingWebhooks
{
    public static void FireAndForget(IServiceScopeFactory scopeFactory, Guid tenantId, string eventType, object data)
    {
        var payload = JsonSerializer.Serialize(new
        {
            @event = eventType,
            tenantId,
            data,
            timestamp = DateTimeOffset.UtcNow
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                await SendToWebhooksAsync(db, httpFactory, tenantId, eventType, payload);
            }
            catch { }
        });
    }

    private static async Task SendToWebhooksAsync(
        AppDbContext db,
        IHttpClientFactory httpFactory,
        Guid tenantId,
        string eventType,
        string payload)
    {
        var webhooks = await db.TenantBillingWebhooks
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.Enabled)
            .ToListAsync();

        if (webhooks.Count == 0) return;

        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        foreach (var webhook in webhooks)
        {
            var subscribedEvents = webhook.Events
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!subscribedEvents.Contains("*") && !subscribedEvents.Contains(eventType))
                continue;

            var sig = ComputeHmac(webhook.Secret, payload);
            var req = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("X-DRM-Event", eventType);
            req.Headers.TryAddWithoutValidation("X-DRM-Signature", $"sha256={sig}");
            req.Headers.TryAddWithoutValidation("X-DRM-Delivery", Guid.NewGuid().ToString());

            try { await http.SendAsync(req); }
            catch { }
        }
    }

    private static string ComputeHmac(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }
}
