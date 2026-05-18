using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminRegistrationsEndpoints
{
    public static IEndpointRouteBuilder MapAdminRegistrationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/registrations");
        group.MapGet("/", ListAsync);
        group.MapPost("/{registrationId:guid}/approve", ApproveAsync);
        group.MapPost("/{registrationId:guid}/reject", RejectAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        string? status,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsRead, out var fail))
            return fail!;

        var query = dbContext.TenantRegistrations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<RegistrationStatus>(status, ignoreCase: true, out var statusFilter))
            query = query.Where(r => r.Status == statusFilter);

        var entities = await query.ToListAsync(cancellationToken);
        var regs = entities
            .OrderByDescending(r => r.RequestedAtUtc)
            .Select(RegistrationRow.From)
            .ToList();

        return Results.Ok(regs);
    }

    private static async Task<IResult> ApproveAsync(
        Guid registrationId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var reg = await dbContext.TenantRegistrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, cancellationToken);

        if (reg is null)
            return Results.NotFound();

        if (reg.Status == RegistrationStatus.Approved)
            return Results.Ok(RegistrationRow.From(reg));

        if (reg.Status == RegistrationStatus.Rejected)
            return Results.BadRequest(new { reasonCode = "already_rejected" });

        if (reg.Status == RegistrationStatus.Pending)
            return Results.BadRequest(new { reasonCode = "email_not_verified" });

        return await PublicRegistrationEndpoints.AutoApproveAsync(reg, dbContext, cancellationToken);
    }

    private static async Task<IResult> RejectAsync(
        Guid registrationId,
        RejectRequest? request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.TenantsWrite, out var fail))
            return fail!;

        var reg = await dbContext.TenantRegistrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, cancellationToken);

        if (reg is null)
            return Results.NotFound();

        if (reg.Status is RegistrationStatus.Approved or RegistrationStatus.Rejected)
            return Results.BadRequest(new { reasonCode = "already_finalized" });

        reg.Status = RegistrationStatus.Rejected;
        reg.ReviewNotes = request?.Notes;
        reg.ReviewedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(RegistrationRow.From(reg));
    }

    private sealed record RejectRequest(string? Notes);

    internal sealed record RegistrationRow(
        Guid RegistrationId,
        string TenantName,
        string DisplayName,
        string AdminEmail,
        string AdminDisplayName,
        int? MaxEncrypters,
        RegistrationStatus Status,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? ReviewedAtUtc,
        string? ReviewNotes,
        Guid? CreatedTenantId,
        Guid? CreatedUserId)
    {
        public static RegistrationRow From(TenantRegistrationEntity r)
            => new(r.RegistrationId, r.TenantName, r.DisplayName, r.AdminEmail, r.AdminDisplayName,
                r.MaxEncrypters, r.Status, r.RequestedAtUtc, r.ReviewedAtUtc,
                r.ReviewNotes, r.CreatedTenantId, r.CreatedUserId);
    }
}
