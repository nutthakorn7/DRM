using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class BoxWebhookEndpoints
{
    private const string PrimarySignatureHeader = "BOX-SIGNATURE-PRIMARY";
    private const string SecondarySignatureHeader = "BOX-SIGNATURE-SECONDARY";
    private const string TenantIdHeader = "X-DRM-Tenant-Id";

    public static IEndpointRouteBuilder MapBoxWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/box/webhook", ReceiveWebhookAsync);
        return endpoints;
    }

    private static async Task<Results<Accepted, NotFound, UnauthorizedHttpResult, BadRequest<ErrorResponse>>> ReceiveWebhookAsync(
        HttpContext context,
        AppDbContext dbContext,
        IBoxIntegrationService boxService,
        CancellationToken cancellationToken)
    {
        if (!context.Request.Headers.TryGetValue(TenantIdHeader, out var tenantHeader) ||
            !Guid.TryParse(tenantHeader.ToString(), out var tenantId))
        {
            return TypedResults.BadRequest(new ErrorResponse("missing_tenant_id"));
        }

        var config = await dbContext.TenantBoxIntegrationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null || !config.Enabled || string.IsNullOrEmpty(config.WebhookSecret))
        {
            return TypedResults.NotFound();
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        context.Request.Body.Position = 0;

        var primary = context.Request.Headers[PrimarySignatureHeader].ToString();
        var secondary = context.Request.Headers[SecondarySignatureHeader].ToString();

        if (!boxService.VerifyWebhookSignature(config.WebhookSecret, body, primary, secondary))
        {
            return TypedResults.Unauthorized();
        }

        var (eventType, sourceItemId, sourceItemName, createdByEmail) = ParsePayload(body);

        dbContext.BoxWebhookEvents.Add(new BoxWebhookEventEntity
        {
            TenantId = tenantId,
            EventType = eventType,
            SourceItemId = sourceItemId,
            SourceItemName = sourceItemName,
            CreatedByEmail = createdByEmail,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });
        config.LastWebhookEventCount++;
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Accepted("/api/box/webhook");
    }

    private static (string EventType, string SourceItemId, string SourceItemName, string? CreatedByEmail) ParsePayload(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("trigger", out var t) ? (t.GetString() ?? "") : "";
            var sourceId = string.Empty;
            var sourceName = string.Empty;
            if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                if (source.TryGetProperty("id", out var idEl)) sourceId = idEl.GetString() ?? "";
                if (source.TryGetProperty("name", out var nameEl)) sourceName = nameEl.GetString() ?? "";
            }
            string? createdByEmail = null;
            if (root.TryGetProperty("created_by", out var creator) && creator.ValueKind == JsonValueKind.Object &&
                creator.TryGetProperty("login", out var loginEl))
            {
                createdByEmail = loginEl.GetString();
            }
            return (eventType, sourceId, sourceName, createdByEmail);
        }
        catch (JsonException)
        {
            return ("unparseable", string.Empty, string.Empty, null);
        }
    }

    private sealed record ErrorResponse(string ReasonCode);
}
