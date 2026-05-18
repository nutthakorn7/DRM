using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminDevicesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDevicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/devices");

        group.MapGet("/", ListDevicesAsync);
        group.MapGet("/health", GetDeviceHealthAsync);
        group.MapPost("/{deviceId:guid}/disable", DisableDeviceAsync);

        return endpoints;
    }

    private static async Task<IResult> ListDevicesAsync(
        Guid tenantId,
        Guid? userId,
        string? status,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.UsersRead, out var fail))
            return fail!;
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

        var devices = await query
            .OrderBy(device => device.Hostname)
            .ThenBy(device => device.DeviceId)
            .Take(500)
            .Select(device => DeviceResponse.From(device))
            .ToListAsync(cancellationToken);
        return Results.Ok(devices);
    }

    private static async Task<IResult> GetDeviceHealthAsync(
        Guid tenantId,
        int? staleAfterMinutes,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.UsersRead, out var fail))
            return fail!;
        var effectiveStaleAfterMinutes = staleAfterMinutes ?? 15;
        if (effectiveStaleAfterMinutes is < 1 or > 10080)
        {
            return Results.BadRequest(new ErrorResponse("invalid_stale_after_minutes"));
        }

        var staleThresholdUtc = DateTimeOffset.UtcNow.AddMinutes(-effectiveStaleAfterMinutes);
        var devices = await dbContext.AgentDevices
            .AsNoTracking()
            .Where(device => device.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var disabled = devices.Count(IsDisabled);
        var activeDevices = devices.Where(device => !IsDisabled(device)).ToList();
        var neverSeen = activeDevices.Count(device => device.LastHeartbeatAtUtc is null);
        var online = activeDevices.Count(device => device.LastHeartbeatAtUtc >= staleThresholdUtc);
        var stale = activeDevices.Count(device => device.LastHeartbeatAtUtc < staleThresholdUtc);
        var newestHeartbeatAtUtc = devices
            .Where(device => device.LastHeartbeatAtUtc is not null)
            .Select(device => device.LastHeartbeatAtUtc)
            .Max();

        return Results.Ok(new DeviceHealthResponse(
            tenantId,
            devices.Count,
            online,
            stale,
            neverSeen,
            disabled,
            effectiveStaleAfterMinutes,
            staleThresholdUtc,
            newestHeartbeatAtUtc));
    }

    private static async Task<IResult> DisableDeviceAsync(
        Guid deviceId,
        DisableDeviceRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermission(httpContext, AdminPermissions.UsersWrite, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

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
            ActorAdminId = AdminAudit.ActorId(httpContext),
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

    private sealed record DeviceHealthResponse(
        Guid TenantId,
        int Total,
        int Online,
        int Stale,
        int NeverSeen,
        int Disabled,
        int StaleAfterMinutes,
        DateTimeOffset StaleThresholdUtc,
        DateTimeOffset? NewestHeartbeatAtUtc);

    private sealed record ErrorResponse(string ReasonCode);

    private static bool IsDisabled(AgentDeviceEntity device)
        => device.DisabledAtUtc is not null || device.Status == "disabled";
}
