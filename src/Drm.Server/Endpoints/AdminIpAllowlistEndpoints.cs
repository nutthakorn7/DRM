using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminIpAllowlistEndpoints
{
    public static IEndpointRouteBuilder MapAdminIpAllowlistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/tenants/{tenantId:guid}/ip-allowlist", ListAsync);
        endpoints.MapPost("/api/admin/tenants/{tenantId:guid}/ip-allowlist", AddAsync);
        endpoints.MapDelete("/api/admin/tenants/{tenantId:guid}/ip-allowlist/{ruleId:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsRead, tenantId, out var fail))
            return fail!;

        // Materialize first — DateTimeOffset ORDER BY on SQLite is unreliable
        var rules = (await dbContext.TenantIpAllowlistRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct))
            .OrderBy(r => r.CreatedAtUtc)
            .ToList();

        return Results.Ok(rules.Select(IpRuleResponse.From));
    }

    private static async Task<IResult> AddAsync(
        Guid tenantId,
        AddIpRuleRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        if (!IpAllowlistService.IsValidCidr(request.Cidr))
            return Results.BadRequest(new { reasonCode = "invalid_cidr" });

        var rule = new TenantIpAllowlistRuleEntity
        {
            RuleId = Guid.NewGuid(),
            TenantId = tenantId,
            Cidr = request.Cidr.Trim(),
            Label = request.Label?.Trim() ?? string.Empty,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.TenantIpAllowlistRules.Add(rule);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/admin/tenants/{tenantId}/ip-allowlist/{rule.RuleId}",
            IpRuleResponse.From(rule));
    }

    private static async Task<IResult> DeleteAsync(
        Guid tenantId,
        Guid ruleId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var rule = await dbContext.TenantIpAllowlistRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RuleId == ruleId, ct);
        if (rule is null) return Results.NotFound();

        dbContext.TenantIpAllowlistRules.Remove(rule);
        await dbContext.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private sealed record AddIpRuleRequest(string Cidr, string? Label);

    private sealed record IpRuleResponse(
        Guid RuleId,
        Guid TenantId,
        string Cidr,
        string Label,
        DateTimeOffset CreatedAtUtc)
    {
        public static IpRuleResponse From(TenantIpAllowlistRuleEntity r)
            => new(r.RuleId, r.TenantId, r.Cidr, r.Label, r.CreatedAtUtc);
    }
}
