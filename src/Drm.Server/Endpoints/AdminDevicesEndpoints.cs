using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminDevicesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDevicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/devices");

        group.MapGet("/", ListDevicesAsync);

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
        DateTimeOffset? LastHeartbeatAtUtc)
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
                device.LastHeartbeatAtUtc);
    }
}
