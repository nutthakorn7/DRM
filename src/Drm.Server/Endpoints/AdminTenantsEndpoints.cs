using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminTenantsEndpoints
{
    public static IEndpointRouteBuilder MapAdminTenantsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/tenants");

        group.MapGet("/", ListTenantsAsync);
        group.MapPost("/", CreateTenantAsync);
        group.MapGet("/{tenantId:guid}", GetTenantAsync);
        group.MapPatch("/{tenantId:guid}", UpdateTenantAsync);

        return endpoints;
    }

    private static async Task<IResult> ListTenantsAsync(
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        var userCounts = await dbContext.TenantUsers
            .AsNoTracking()
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

        var tenants = await dbContext.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(tenants.Select(t =>
            TenantResponse.From(t, userCounts.GetValueOrDefault(t.TenantId, 0))));
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var tenantId = request.TenantId ?? Guid.NewGuid();

        if (await dbContext.Tenants.AnyAsync(t => t.TenantId == tenantId || t.Name == request.Name, cancellationToken))
            return Results.Conflict();

        var tenant = new TenantEntity
        {
            TenantId = tenantId,
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Status = TenantStatus.Active,
            MaxEncrypters = request.MaxEncrypters,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.Tenants.Add(tenant);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await dbContext.Tenants.AnyAsync(t => t.TenantId == tenantId || t.Name == request.Name, cancellationToken))
                return Results.Conflict();
            throw;
        }

        return Results.Created($"/api/admin/tenants/{tenant.TenantId}", TenantResponse.From(tenant, 0));
    }

    private static async Task<IResult> GetTenantAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

        if (tenant is null)
            return Results.NotFound();

        var usedSeats = await dbContext.TenantUsers
            .AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId, cancellationToken);

        return Results.Ok(TenantResponse.From(tenant, usedSeats));
    }

    private static async Task<IResult> UpdateTenantAsync(
        Guid tenantId,
        UpdateTenantRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var tenant = await dbContext.Tenants
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

        if (tenant is null)
            return Results.NotFound();

        if (request.DisplayName is not null)
            tenant.DisplayName = request.DisplayName;

        if (request.MaxEncrypters is not null)
            tenant.MaxEncrypters = request.MaxEncrypters == 0 ? null : request.MaxEncrypters;

        var wasActive = tenant.Status == TenantStatus.Active;
        if (request.Status is not null && request.Status != tenant.Status)
        {
            tenant.Status = request.Status.Value;
            tenant.SuspendedAtUtc = request.Status == TenantStatus.Suspended ? DateTimeOffset.UtcNow : null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasActive && tenant.Status == TenantStatus.Suspended)
            BillingWebhooks.FireAndForget(scopeFactory, tenantId, "tenant_suspended",
                new { tenantId, suspendedAtUtc = tenant.SuspendedAtUtc });

        var usedSeats = await dbContext.TenantUsers
            .AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId, cancellationToken);

        return Results.Ok(TenantResponse.From(tenant, usedSeats));
    }

    private sealed record CreateTenantRequest(string Name, string? DisplayName, Guid? TenantId, int? MaxEncrypters);

    private sealed record UpdateTenantRequest(string? DisplayName, TenantStatus? Status, int? MaxEncrypters);

    internal sealed record TenantResponse(
        Guid TenantId,
        string Name,
        string DisplayName,
        TenantStatus Status,
        int? MaxEncrypters,
        int UsedSeats,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? SuspendedAtUtc)
    {
        public static TenantResponse From(TenantEntity t, int usedSeats)
            => new(t.TenantId, t.Name, t.DisplayName, t.Status, t.MaxEncrypters, usedSeats, t.CreatedAtUtc, t.SuspendedAtUtc);
    }
}
