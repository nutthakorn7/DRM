using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminTenantBillingWebhooksEndpoints
{
    private static readonly HashSet<string> KnownEvents =
        new(StringComparer.OrdinalIgnoreCase) { "*", "seat_limit_approach", "tenant_suspended" };

    public static IEndpointRouteBuilder MapAdminTenantBillingWebhooksEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/tenants/{tenantId:guid}/billing-webhooks");
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapDelete("/{webhookId:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        if (!await TenantExistsAsync(dbContext, tenantId, cancellationToken))
            return Results.NotFound();

        var webhooks = await dbContext.TenantBillingWebhooks
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .OrderBy(w => w.WebhookId)
            .Select(w => WebhookResponse.From(w))
            .ToListAsync(cancellationToken);

        return Results.Ok(webhooks);
    }

    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreateWebhookRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        if (string.IsNullOrWhiteSpace(request.Url) ||
            !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return Results.BadRequest(new { reasonCode = "invalid_url" });

        if (!await TenantExistsAsync(dbContext, tenantId, cancellationToken))
            return Results.NotFound();

        var secret = string.IsNullOrWhiteSpace(request.Secret)
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            : request.Secret.Trim();

        var entity = new TenantBillingWebhookEntity
        {
            TenantId = tenantId,
            WebhookId = Guid.NewGuid(),
            Url = request.Url.Trim(),
            Secret = secret,
            Events = NormalizeEvents(request.Events),
            Enabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.TenantBillingWebhooks.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/admin/tenants/{tenantId}/billing-webhooks/{entity.WebhookId}",
            new WebhookCreateResponse(
                entity.WebhookId, entity.TenantId, entity.Url,
                entity.Events, entity.Enabled, entity.CreatedAtUtc, secret));
    }

    private static async Task<IResult> DeleteAsync(
        Guid tenantId,
        Guid webhookId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var webhook = await dbContext.TenantBillingWebhooks
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.WebhookId == webhookId, cancellationToken);

        if (webhook is null)
            return Results.NotFound();

        dbContext.TenantBillingWebhooks.Remove(webhook);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static Task<bool> TenantExistsAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
        => db.Tenants.AnyAsync(t => t.TenantId == tenantId, ct);

    private static string NormalizeEvents(string? events)
    {
        if (string.IsNullOrWhiteSpace(events)) return "*";
        var parts = events
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => KnownEvents.Contains(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count > 0 ? string.Join(",", parts) : "*";
    }

    private sealed record CreateWebhookRequest(string? Url, string? Secret, string? Events);

    private sealed record WebhookResponse(
        Guid WebhookId,
        Guid TenantId,
        string Url,
        string Events,
        bool Enabled,
        DateTimeOffset CreatedAtUtc)
    {
        public static WebhookResponse From(TenantBillingWebhookEntity w)
            => new(w.WebhookId, w.TenantId, w.Url, w.Events, w.Enabled, w.CreatedAtUtc);
    }

    private sealed record WebhookCreateResponse(
        Guid WebhookId,
        Guid TenantId,
        string Url,
        string Events,
        bool Enabled,
        DateTimeOffset CreatedAtUtc,
        string Secret);
}
