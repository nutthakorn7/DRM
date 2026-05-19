using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminKeyRotationEndpoints
{
    public static IEndpointRouteBuilder MapAdminKeyRotationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/tenants/{tenantId:guid}/key-rotation", GetConfigAsync);
        endpoints.MapPut("/api/admin/tenants/{tenantId:guid}/key-rotation", UpsertConfigAsync);
        endpoints.MapPost("/api/admin/tenants/{tenantId:guid}/key-rotation/trigger", TriggerAsync);
        endpoints.MapGet("/api/admin/tenants/{tenantId:guid}/key-rotation/history", GetHistoryAsync);
        return endpoints;
    }

    private static async Task<IResult> GetConfigAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsRead, tenantId, out var fail))
            return fail!;

        var config = await dbContext.TenantKeyRotationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        return Results.Ok(KeyRotationConfigResponse.From(config, tenantId));
    }

    private static async Task<IResult> UpsertConfigAsync(
        Guid tenantId,
        UpsertKeyRotationRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        var config = await dbContext.TenantKeyRotationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        if (config is null)
        {
            config = new TenantKeyRotationConfigEntity { TenantId = tenantId };
            dbContext.TenantKeyRotationConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.IntervalDays = request.IntervalDays;
        config.NextRotationDueUtc = config.LastRotatedAtUtc.HasValue
            ? config.LastRotatedAtUtc.Value.AddDays(request.IntervalDays)
            : DateTimeOffset.UtcNow.AddDays(request.IntervalDays);
        config.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(KeyRotationConfigResponse.From(config, tenantId));
    }

    private static async Task<IResult> TriggerAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var count = await KeyRotationService.RotateTenantKeysAsync(dbContext, tenantId, "manual", ct);
        return Results.Ok(new { TenantId = tenantId, FilesRotated = count, RotatedAtUtc = DateTimeOffset.UtcNow });
    }

    private static async Task<IResult> GetHistoryAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        int? limit,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsRead, tenantId, out var fail))
            return fail!;

        var cap = Math.Min(limit ?? 20, 100);
        var all = await dbContext.KeyRotationHistory.AsNoTracking()
            .Where(h => h.TenantId == tenantId)
            .ToListAsync(ct);
        var history = all.OrderByDescending(h => h.RotatedAtUtc).Take(cap).ToList();

        return Results.Ok(history.Select(h => new
        {
            h.Id, h.TenantId, h.FilesRotated, h.TriggeredBy, h.RotatedAtUtc
        }));
    }

    private sealed record UpsertKeyRotationRequest(bool Enabled, int IntervalDays);

    private sealed record KeyRotationConfigResponse(
        Guid TenantId, bool Enabled, int IntervalDays,
        DateTimeOffset? LastRotatedAtUtc, DateTimeOffset? NextRotationDueUtc)
    {
        public static KeyRotationConfigResponse From(TenantKeyRotationConfigEntity? config, Guid tenantId) =>
            config is null
                ? new(tenantId, false, 90, null, null)
                : new(tenantId, config.Enabled, config.IntervalDays,
                    config.LastRotatedAtUtc, config.NextRotationDueUtc);
    }
}
