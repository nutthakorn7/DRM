using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminRetentionPolicyEndpoints
{
    public static IEndpointRouteBuilder MapAdminRetentionPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/tenants/{tenantId:guid}/retention-policy", GetAsync);
        endpoints.MapPut("/api/admin/tenants/{tenantId:guid}/retention-policy", UpsertAsync);
        endpoints.MapPost("/api/admin/tenants/{tenantId:guid}/retention-policy/apply", ApplyAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsRead, tenantId, out var fail))
            return fail!;

        var policy = await dbContext.TenantRetentionPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        return Results.Ok(RetentionPolicyResponse.From(policy, tenantId));
    }

    private static async Task<IResult> UpsertAsync(
        Guid tenantId,
        UpsertRetentionPolicyRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        var policy = await dbContext.TenantRetentionPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        if (policy is null)
        {
            policy = new TenantRetentionPolicyEntity { TenantId = tenantId };
            dbContext.TenantRetentionPolicies.Add(policy);
        }

        policy.Enabled = request.Enabled;
        policy.FileRetentionDays = request.FileRetentionDays;
        policy.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(RetentionPolicyResponse.From(policy, tenantId));
    }

    private static async Task<IResult> ApplyAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        bool? dryRun,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var policy = await dbContext.TenantRetentionPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);
        if (policy is null || !policy.Enabled || !policy.FileRetentionDays.HasValue)
            return Results.BadRequest(new { reasonCode = "no_active_policy" });

        var (deleted, candidates) = await DataRetentionService.ApplyTenantRetentionAsync(
            dbContext, tenantId, policy.FileRetentionDays.Value, dryRun ?? false, ct);

        return Results.Ok(new
        {
            TenantId = tenantId,
            DryRun = dryRun ?? false,
            FilesDeleted = deleted,
            FilesCandidates = candidates
        });
    }

    private sealed record UpsertRetentionPolicyRequest(bool Enabled, int? FileRetentionDays);

    private sealed record RetentionPolicyResponse(
        Guid TenantId, bool Enabled, int? FileRetentionDays, DateTimeOffset? UpdatedAtUtc)
    {
        public static RetentionPolicyResponse From(TenantRetentionPolicyEntity? p, Guid tenantId) =>
            p is null ? new(tenantId, false, null, null)
                      : new(tenantId, p.Enabled, p.FileRetentionDays, p.UpdatedAtUtc);
    }
}
