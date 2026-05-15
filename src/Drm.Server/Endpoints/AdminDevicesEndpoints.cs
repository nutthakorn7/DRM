using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminDevicesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDevicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/devices");

        group.MapGet("/", ListDevicesAsync);
        group.MapPost("/{deviceId:guid}/disable", DisableDeviceAsync);

        return endpoints;
    }

    private static async Task<IReadOnlyList<DeviceResponse>> ListDevicesAsync(
        Guid tenantId,
        Guid? userId,
        string? status,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AgentDevices
            .AsNoTracking()
            .Where(device => device.TenantId == tenantId);

        if (userId is not null && userId != Guid.Empty)
        {
            query = query.Where(device => device.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(device => device.Status == status.Trim());
        }

        return await query
            .OrderBy(device => device.Hostname)
            .ThenBy(device => device.DeviceId)
            .Take(500)
            .Select(device => DeviceResponse.From(device))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IResult> DisableDeviceAsync(
        Guid deviceId,
        DisableDeviceRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new ErrorResponse("invalid_disable_reason"));
        }

        var device = await dbContext.AgentDevices
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.DeviceId == deviceId,
                cancellationToken);

        if (device is null)
        {
            return Results.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var reason = request.Reason.Trim();
        device.Status = "disabled";
        device.DisabledAtUtc ??= now;
        device.DisabledReason = reason;
        device.UpdatedAtUtc = now;

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            UserId = request.AdminUserId,
            EventType = "device_disabled",
            ReasonCode = reason,
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(DeviceResponse.From(device));
    }

    private sealed record DeviceResponse(
        Guid TenantId,
        Guid DeviceId,
        Guid UserId,
        string Hostname,
        string OperatingSystem,
        string AgentVersion,
        string Status,
        DateTimeOffset RegisteredAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? LastHeartbeatAtUtc,
        DateTimeOffset? DisabledAtUtc,
        string? DisabledReason)
    {
        public static DeviceResponse From(AgentDeviceEntity device)
            => new(
                device.TenantId,
                device.DeviceId,
                device.UserId,
                device.Hostname,
                device.OperatingSystem,
                device.AgentVersion,
                device.Status,
                device.RegisteredAtUtc,
                device.UpdatedAtUtc,
                device.LastHeartbeatAtUtc,
                device.DisabledAtUtc,
                device.DisabledReason);
    }

    private sealed record DisableDeviceRequest(Guid TenantId, Guid AdminUserId, string Reason);

    private sealed record ErrorResponse(string ReasonCode);
}
