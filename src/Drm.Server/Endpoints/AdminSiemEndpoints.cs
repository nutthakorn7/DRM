using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminSiemEndpoints
{
    private const int MaxUrlLength = 2048;

    public static IEndpointRouteBuilder MapAdminSiemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/siem-webhooks");

        group.MapPost("/", CreateWebhookAsync);
        group.MapGet("/", ListWebhooksAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateWebhookAsync(
        CreateSiemWebhookRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        var validationError = await ValidateCreateRequestAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return TypedResults.BadRequest(validationError);
        }

        if (await WebhookExistsAsync(dbContext, request.TenantId, request.WebhookId, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        var webhook = new SiemWebhookEntity
        {
            TenantId = request.TenantId,
            WebhookId = request.WebhookId,
            Url = request.Url!.Trim(),
            Enabled = request.Enabled,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.SiemWebhooks.Add(webhook);
        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(request.TenantId, null, "siem_webhook_created", httpContext));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await WebhookExistsAsync(dbContext, request.TenantId, request.WebhookId, cancellationToken))
                return Results.Conflict();
            throw;
        }

        return Results.Created(
            $"/api/admin/siem-webhooks/{webhook.WebhookId}?tenantId={webhook.TenantId}",
            SiemWebhookResponse.From(webhook));
    }

    private static async Task<IResult> ListWebhooksAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.SettingsRead, out var fail))
            return fail!;
        if (tenantId == Guid.Empty)
            return Results.BadRequest(new ErrorResponse("invalid_tenant_id"));

        var webhooks = await dbContext.SiemWebhooks
            .AsNoTracking()
            .Where(webhook => webhook.TenantId == tenantId)
            .OrderBy(webhook => webhook.Url)
            .ThenBy(webhook => webhook.WebhookId)
            .Select(webhook => SiemWebhookResponse.From(webhook))
            .ToListAsync(cancellationToken);

        return Results.Ok(webhooks);
    }

    private static Task<bool> WebhookExistsAsync(
        AppDbContext dbContext,
        Guid tenantId,
        Guid webhookId,
        CancellationToken cancellationToken)
    {
        return dbContext.SiemWebhooks
            .AsNoTracking()
            .AnyAsync(webhook => webhook.TenantId == tenantId && webhook.WebhookId == webhookId, cancellationToken);
    }

    private static async Task<ErrorResponse?> ValidateCreateRequestAsync(
        CreateSiemWebhookRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return new ErrorResponse("invalid_tenant_id");
        }

        if (request.WebhookId == Guid.Empty)
        {
            return new ErrorResponse("invalid_webhook_id");
        }

        var url = request.Url?.Trim();
        if (string.IsNullOrEmpty(url) || url.Length > MaxUrlLength)
        {
            return new ErrorResponse("invalid_url");
        }

        try
        {
            if (!await SiemWebhookUrlGuard.IsAllowedAsync(url, cancellationToken))
            {
                return new ErrorResponse("invalid_url");
            }
        }
        catch (System.Net.Sockets.SocketException)
        {
            return new ErrorResponse("invalid_url");
        }

        return null;
    }

    private sealed record CreateSiemWebhookRequest(
        Guid TenantId,
        Guid WebhookId,
        string? Url,
        bool Enabled);

    private sealed record SiemWebhookResponse(
        Guid TenantId,
        Guid WebhookId,
        string Url,
        bool Enabled,
        DateTimeOffset CreatedAtUtc)
    {
        public static SiemWebhookResponse From(SiemWebhookEntity webhook)
            => new(
                webhook.TenantId,
                webhook.WebhookId,
                webhook.Url,
                webhook.Enabled,
                webhook.CreatedAtUtc);
    }

    private sealed record ErrorResponse(string ReasonCode);
}
