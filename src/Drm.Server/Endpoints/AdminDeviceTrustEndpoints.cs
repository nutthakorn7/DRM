using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminDeviceTrustEndpoints
{
    public static IEndpointRouteBuilder MapAdminDeviceTrustEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/tenants/{tenantId:guid}/device-trust", GetAsync);
        endpoints.MapPut("/api/admin/tenants/{tenantId:guid}/device-trust", UpsertAsync);
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

        var config = await dbContext.TenantDeviceTrustConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

        return Results.Ok(DeviceTrustResponse.From(config, tenantId));
    }

    private static async Task<IResult> UpsertAsync(
        Guid tenantId,
        UpsertDeviceTrustRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.TenantsWrite, tenantId, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is null) return Results.NotFound();

        if (request.RequiredCheckinDays < 1 || request.RequiredCheckinDays > 365)
            return Results.BadRequest(new { reasonCode = "invalid_checkin_days" });

        var allowedDomains = NormalizeDomains(request.AllowedAdDomains);
        if (request.RequireDomainJoined && allowedDomains.Count == 0)
            return Results.BadRequest(new { reasonCode = "allowed_ad_domains_required" });

        var config = await dbContext.TenantDeviceTrustConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        if (config is null)
        {
            config = new TenantDeviceTrustConfigEntity { TenantId = tenantId };
            dbContext.TenantDeviceTrustConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.RequiredCheckinDays = request.RequiredCheckinDays;
        config.RequireDomainJoined = request.RequireDomainJoined;
        config.AllowedAdDomainsCsv = string.Join(',', allowedDomains);
        config.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(DeviceTrustResponse.From(config, tenantId));
    }

    private static IReadOnlyList<string> NormalizeDomains(IReadOnlyList<string>? domains)
        => (domains ?? [])
            .Select(domain => domain.Trim().ToUpperInvariant())
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> ParseDomains(string domainsCsv)
        => NormalizeDomains(domainsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries));

    private sealed record UpsertDeviceTrustRequest(
        bool Enabled,
        int RequiredCheckinDays,
        bool RequireDomainJoined,
        IReadOnlyList<string>? AllowedAdDomains);

    private sealed record DeviceTrustResponse(
        Guid TenantId,
        bool Enabled,
        int RequiredCheckinDays,
        bool RequireDomainJoined,
        IReadOnlyList<string> AllowedAdDomains,
        DateTimeOffset? UpdatedAtUtc)
    {
        public static DeviceTrustResponse From(TenantDeviceTrustConfigEntity? c, Guid tenantId)
            => c is null
                ? new(tenantId, false, 7, false, [], null)
                : new(
                    tenantId,
                    c.Enabled,
                    c.RequiredCheckinDays,
                    c.RequireDomainJoined,
                    ParseDomains(c.AllowedAdDomainsCsv),
                    c.UpdatedAtUtc);
    }
}
