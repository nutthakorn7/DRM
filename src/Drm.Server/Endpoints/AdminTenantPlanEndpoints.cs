using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminTenantPlanEndpoints
{
    private static readonly Dictionary<string, (PlanTier tier, int? maxFiles, int? maxStorageMb)> Presets = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["free"]       = (PlanTier.Free,       maxFiles:    50, maxStorageMb:    500),
        ["starter"]    = (PlanTier.Starter,    maxFiles:  1000, maxStorageMb:  10000),
        ["enterprise"] = (PlanTier.Enterprise, maxFiles:  null, maxStorageMb:   null),
    };

    public static IEndpointRouteBuilder MapAdminTenantPlanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/tenants/{tenantId:guid}/plan",  GetPlanAsync);
        endpoints.MapPut("/api/admin/tenants/{tenantId:guid}/plan",  UpsertPlanAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPlanAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsRead, tenantId, out var fail))
            return fail!;

        var plan = await dbContext.TenantPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);

        var fileCount = await dbContext.ProtectedFiles.AsNoTracking()
            .CountAsync(f => f.TenantId == tenantId, cancellationToken);

        return Results.Ok(PlanResponse.From(plan, tenantId, fileCount));
    }

    private static async Task<IResult> UpsertPlanAsync(
        Guid tenantId,
        UpsertPlanRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
        if (tenant is null) return Results.NotFound();

        // Apply preset if provided
        int? maxFiles = request.MaxFiles;
        int? maxStorageMb = request.MaxStorageMb;
        PlanTier tier;

        if (request.Preset is not null && Presets.TryGetValue(request.Preset, out var preset))
        {
            tier = preset.tier;
            maxFiles = preset.maxFiles;
            maxStorageMb = preset.maxStorageMb;
        }
        else if (!Enum.TryParse<PlanTier>(request.Tier, ignoreCase: true, out tier))
        {
            return Results.BadRequest(new { reasonCode = "invalid_tier" });
        }

        var plan = await dbContext.TenantPlans.FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        if (plan is null)
        {
            plan = new TenantPlanEntity { TenantId = tenantId };
            dbContext.TenantPlans.Add(plan);
        }

        plan.Tier = tier;
        plan.MaxFiles = maxFiles;
        plan.MaxStorageMb = maxStorageMb;
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var fileCount = await dbContext.ProtectedFiles.AsNoTracking()
            .CountAsync(f => f.TenantId == tenantId, cancellationToken);

        return Results.Ok(PlanResponse.From(plan, tenantId, fileCount));
    }

    private sealed record UpsertPlanRequest(string? Tier, int? MaxFiles, int? MaxStorageMb, string? Preset);

    private sealed record PlanResponse(
        Guid TenantId,
        string Tier,
        int? MaxFiles,
        int? MaxStorageMb,
        int CurrentFileCount,
        bool FilesQuotaExceeded,
        DateTimeOffset? UpdatedAtUtc)
    {
        public static PlanResponse From(TenantPlanEntity? plan, Guid tenantId, int fileCount) =>
            plan is null
                ? new(tenantId, PlanTier.Free.ToString().ToLowerInvariant(),
                    50, 500, fileCount, fileCount > 50, null)
                : new(tenantId, plan.Tier.ToString().ToLowerInvariant(),
                    plan.MaxFiles, plan.MaxStorageMb, fileCount,
                    plan.MaxFiles.HasValue && fileCount > plan.MaxFiles.Value,
                    plan.UpdatedAtUtc);
    }
}
