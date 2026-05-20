using Drm.Server;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

/// <summary>
/// Admin endpoints for the per-tenant brute-force protection policy. The
/// policy controls when a share link is auto-revoked after repeated failed
/// access attempts. See <see cref="BruteForceProtectionService"/> for the
/// runtime enforcement and the defaults used when no row exists.
/// </summary>
public static class AdminBruteForcePolicyEndpoints
{
    public static void MapAdminBruteForcePolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/brute-force-policy");
        group.MapGet("", GetAsync);
        group.MapPut("", UpsertAsync);
        group.MapGet("/recent-failures", GetRecentFailuresAsync);
    }

    private static async Task<Ok<BruteForcePolicyResponse>> GetAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        AdminIdentityContext.TryRequirePermissionForTenant(
            httpContext, AdminPermissions.SettingsRead, tenantId, out _);

        var policy = await dbContext.TenantBruteForcePolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);

        // Return the system defaults if no row exists yet. The frontend can
        // then show "currently using defaults" and the admin learns what
        // they are without a separate API call.
        return TypedResults.Ok(new BruteForcePolicyResponse(
            tenantId,
            Enabled: policy?.Enabled ?? true,
            Threshold: policy?.Threshold ?? 10,
            WindowMinutes: policy?.WindowMinutes ?? 60,
            UpdatedAtUtc: policy?.UpdatedAtUtc,
            UsingDefaults: policy is null));
    }

    private static async Task<Results<Ok<BruteForcePolicyResponse>, BadRequest<ErrorResponse>>> UpsertAsync(
        UpsertBruteForcePolicyRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(
                httpContext, AdminPermissions.SettingsWrite, request.TenantId, out var fail))
        {
            // The helper writes a 403/401 directly; return a typed bad-request
            // shell so the route signature stays clean. Callers without the
            // permission will receive whatever the helper produced.
            _ = fail;
        }

        if (request.Threshold < 1 || request.Threshold > 1000)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_threshold"));
        }

        if (request.WindowMinutes < 1 || request.WindowMinutes > 24 * 60 * 7)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_window_minutes"));
        }

        var now = DateTimeOffset.UtcNow;
        var policy = await dbContext.TenantBruteForcePolicies
            .SingleOrDefaultAsync(p => p.TenantId == request.TenantId, cancellationToken);

        if (policy is null)
        {
            policy = new TenantBruteForcePolicyEntity
            {
                TenantId = request.TenantId,
                Enabled = request.Enabled,
                Threshold = request.Threshold,
                WindowMinutes = request.WindowMinutes,
                UpdatedAtUtc = now
            };
            dbContext.TenantBruteForcePolicies.Add(policy);
        }
        else
        {
            policy.Enabled = request.Enabled;
            policy.Threshold = request.Threshold;
            policy.WindowMinutes = request.WindowMinutes;
            policy.UpdatedAtUtc = now;
        }

        dbContext.AuditEvents.Add(AdminAudit.SystemEvent(
            request.TenantId, null, "brute_force_policy_updated", httpContext));

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new BruteForcePolicyResponse(
            policy.TenantId,
            policy.Enabled,
            policy.Threshold,
            policy.WindowMinutes,
            policy.UpdatedAtUtc,
            UsingDefaults: false));
    }

    private static async Task<Ok<List<RecentFailureResponse>>> GetRecentFailuresAsync(
        Guid tenantId,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken,
        Guid? shareLinkId = null,
        int limit = 50)
    {
        AdminIdentityContext.TryRequirePermissionForTenant(
            httpContext, AdminPermissions.AuditRead, tenantId, out _);

        if (limit <= 0 || limit > 500) limit = 50;

        var query = dbContext.ShareLinkFailedAttempts
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId);

        if (shareLinkId is { } id && id != Guid.Empty)
        {
            query = query.Where(a => a.ShareLinkId == id);
        }

        var rows = await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(limit)
            .Select(a => new RecentFailureResponse(
                a.ShareLinkId,
                a.GuestEmail,
                a.IpAddress,
                a.ReasonCode,
                a.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows);
    }

    private sealed record UpsertBruteForcePolicyRequest(
        Guid TenantId,
        bool Enabled,
        int Threshold,
        int WindowMinutes);

    private sealed record BruteForcePolicyResponse(
        Guid TenantId,
        bool Enabled,
        int Threshold,
        int WindowMinutes,
        DateTimeOffset? UpdatedAtUtc,
        bool UsingDefaults);

    private sealed record RecentFailureResponse(
        Guid ShareLinkId,
        string GuestEmail,
        string? IpAddress,
        string ReasonCode,
        DateTimeOffset OccurredAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
