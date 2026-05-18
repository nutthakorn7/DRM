using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminBoxIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapAdminBoxIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/box");

        group.MapPut("/config", UpsertConfigAsync);
        group.MapGet("/config", GetConfigAsync);
        group.MapPost("/test-connection", TestConnectionAsync);
        group.MapGet("/events", ListEventsAsync);

        return endpoints;
    }

    private static async Task<IResult> UpsertConfigAsync(
        BoxConfigRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

        var existing = await dbContext.TenantBoxIntegrationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);

        var isNew = existing is null;
        if (existing is null)
        {
            existing = new TenantBoxIntegrationConfigEntity { TenantId = request.TenantId };
            dbContext.TenantBoxIntegrationConfigs.Add(existing);
        }

        existing.ClientId = request.ClientId;
        existing.ClientSecret = request.ClientSecret;
        existing.EnterpriseId = request.EnterpriseId;
        existing.WebhookSecret = request.WebhookSecret;
        existing.Enabled = request.Enabled;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = BoxConfigResponse.From(existing);
        return isNew
            ? Results.Created("/api/admin/box/config", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetConfigAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsRead, out var fail))
            return fail!;
        var config = await dbContext.TenantBoxIntegrationConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        return config is null ? Results.NotFound() : Results.Ok(BoxConfigResponse.From(config));
    }

    private static async Task<IResult> TestConnectionAsync(
        TestConnectionRequest request,
        HttpContext httpContext,
        IBoxIntegrationService boxService,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        var result = await boxService.TestConnectionAsync(request.TenantId, cancellationToken);
        return Results.Ok(new BoxConnectionResponse(result.Success, result.Status, result.ErrorMessage));
    }

    private static async Task<IResult> ListEventsAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.IntegrationsRead, out var fail))
            return fail!;
        var pageSize = Math.Clamp(limit ?? 50, 1, 200);
        var events = await dbContext.BoxWebhookEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.Id)
            .Take(pageSize)
            .Select(e => new BoxEventResponse(
                e.Id, e.EventType, e.SourceItemId, e.SourceItemName, e.CreatedByEmail, e.ReceivedAtUtc))
            .ToListAsync(cancellationToken);
        return Results.Ok(events);
    }

    private sealed record BoxConfigRequest(
        Guid TenantId,
        string ClientId,
        string ClientSecret,
        string EnterpriseId,
        string WebhookSecret,
        bool Enabled);

    private sealed record TestConnectionRequest(Guid TenantId);

    private sealed record BoxConfigResponse(
        Guid TenantId,
        string ClientId,
        string EnterpriseId,
        bool Enabled,
        string? LastConnectionStatus,
        DateTimeOffset? LastConnectionAtUtc,
        int LastWebhookEventCount,
        DateTimeOffset UpdatedAtUtc)
    {
        public static BoxConfigResponse From(TenantBoxIntegrationConfigEntity c)
            => new(c.TenantId, c.ClientId, c.EnterpriseId, c.Enabled,
                   c.LastConnectionStatus, c.LastConnectionAtUtc, c.LastWebhookEventCount, c.UpdatedAtUtc);
    }

    private sealed record BoxConnectionResponse(bool Success, string Status, string? ErrorMessage);

    private sealed record BoxEventResponse(
        long Id,
        string EventType,
        string SourceItemId,
        string SourceItemName,
        string? CreatedByEmail,
        DateTimeOffset ReceivedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
