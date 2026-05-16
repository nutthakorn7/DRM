using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminNotificationConfigEndpoints
{
    public static IEndpointRouteBuilder MapAdminNotificationConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/notification-config");

        group.MapPut("/", UpsertConfigAsync);
        group.MapGet("/", GetConfigAsync);

        return endpoints;
    }

    private static async Task<Results<Created<NotificationConfigResponse>, Ok<NotificationConfigResponse>>> UpsertConfigAsync(
        NotificationConfigRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.TenantAdminNotificationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == request.TenantId, cancellationToken);

        bool isNew = existing == null;

        if (existing == null)
        {
            existing = new TenantAdminNotificationConfigEntity { TenantId = request.TenantId };
            dbContext.TenantAdminNotificationConfigs.Add(existing);
        }

        existing.AdminEmailsCsv = request.AdminEmailsCsv;
        existing.NotifyOnExternalShareViewed = request.NotifyOnExternalShareViewed;
        existing.NotifyOnFileRevoked = request.NotifyOnFileRevoked;
        existing.NotifyOnAccessDenied = request.NotifyOnAccessDenied;
        existing.NotifyOnShareLinkCreated = request.NotifyOnShareLinkCreated;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = NotificationConfigResponse.From(existing);
        return isNew
            ? TypedResults.Created("/api/admin/notification-config", response)
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<NotificationConfigResponse>, NotFound>> GetConfigAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.TenantAdminNotificationConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        return config == null
            ? TypedResults.NotFound()
            : TypedResults.Ok(NotificationConfigResponse.From(config));
    }

    private sealed record NotificationConfigRequest(
        Guid TenantId,
        string AdminEmailsCsv,
        bool NotifyOnExternalShareViewed,
        bool NotifyOnFileRevoked,
        bool NotifyOnAccessDenied,
        bool NotifyOnShareLinkCreated);

    private sealed record NotificationConfigResponse(
        Guid TenantId,
        string AdminEmailsCsv,
        bool NotifyOnExternalShareViewed,
        bool NotifyOnFileRevoked,
        bool NotifyOnAccessDenied,
        bool NotifyOnShareLinkCreated,
        DateTimeOffset UpdatedAtUtc)
    {
        public static NotificationConfigResponse From(TenantAdminNotificationConfigEntity c)
            => new(c.TenantId, c.AdminEmailsCsv, c.NotifyOnExternalShareViewed,
                   c.NotifyOnFileRevoked, c.NotifyOnAccessDenied, c.NotifyOnShareLinkCreated, c.UpdatedAtUtc);
    }
}
