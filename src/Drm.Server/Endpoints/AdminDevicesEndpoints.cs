using Drm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AdminDevicesEndpoints
{
    public static IEndpointRouteBuilder MapAdminDevicesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/devices");

        group.MapGet("/", ListDevicesAsync);
        group.MapGet("/health", GetDeviceHealthAsync);
        group.MapPost("/provision", ProvisionDeviceAsync);
        group.MapPost("/{deviceId:guid}/disable", DisableDeviceAsync);
        group.MapPost("/{deviceId:guid}/enable", EnableDeviceAsync);

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
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.UsersRead, tenantId, out var fail))
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
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.UsersRead, tenantId, out var fail))
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
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.UsersWrite, request.TenantId, out var fail))
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

    private static async Task<IResult> ProvisionDeviceAsync(
        ProvisionDeviceRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.UsersWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));
        if (request.UserId == Guid.Empty)
            return Results.BadRequest(new ErrorResponse("invalid_user_id"));

        var deviceId = request.DeviceId is null || request.DeviceId == Guid.Empty
            ? Guid.NewGuid()
            : request.DeviceId.Value;
        var deviceSecret = DeviceRequestSigning.GenerateDeviceSecret();
        var now = DateTimeOffset.UtcNow;
        var device = await dbContext.AgentDevices
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.DeviceId == deviceId,
                cancellationToken);

        if (device is null)
        {
            device = new AgentDeviceEntity
            {
                TenantId = request.TenantId,
                DeviceId = deviceId,
                RegisteredAtUtc = now
            };
            dbContext.AgentDevices.Add(device);
        }
        else if (IsDisabled(device))
        {
            return Results.Json(new ErrorResponse("device_disabled"), statusCode: StatusCodes.Status403Forbidden);
        }

        device.UserId = request.UserId;
        device.Hostname = NormalizeOrExisting(request.Hostname, device.Hostname);
        device.OperatingSystem = NormalizeOrExisting(request.OperatingSystem, device.OperatingSystem);
        device.AgentVersion = NormalizeOrExisting(request.AgentVersion, device.AgentVersion);
        device.Status = "provisioned";
        device.UpdatedAtUtc = now;
        device.DeviceSigningKeyHashBase64 = DeviceRequestSigning.DeriveSigningKeyHashBase64(deviceSecret);
        device.DeviceSigningKeyUpdatedAtUtc = now;

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            UserId = request.UserId,
            ActorAdminId = AdminAudit.ActorId(httpContext),
            EventType = "device_provisioned",
            ReasonCode = "provisioned",
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new ProvisionDeviceResponse(
            request.TenantId,
            request.UserId,
            deviceId,
            deviceSecret,
            now));
    }

    private static async Task<IResult> EnableDeviceAsync(
        Guid deviceId,
        EnableDeviceRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!AdminIdentityContext.TryRequirePermissionForTenant(httpContext, AdminPermissions.UsersWrite, request.TenantId, out var fail))
            return fail!;
        if (!httpContext.MatchesHeader(request.TenantId))
            return Results.BadRequest(new ErrorResponse("tenant_mismatch"));

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
        // Mirror of disable: clear the disabled markers and restore the device to
        // the same active status a fresh agent registration writes ("registered"),
        // so IsDisabled() (DisabledAtUtc != null || Status == "disabled") is false.
        device.Status = "registered";
        device.DisabledAtUtc = null;
        device.DisabledReason = null;
        device.UpdatedAtUtc = now;

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            UserId = request.AdminUserId,
            ActorAdminId = AdminAudit.ActorId(httpContext),
            EventType = "device_enabled",
            ReasonCode = "device_re_enabled",
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

    private sealed record ProvisionDeviceRequest(
        Guid TenantId,
        Guid UserId,
        Guid? DeviceId,
        string? Hostname,
        string? OperatingSystem,
        string? AgentVersion);

    private sealed record ProvisionDeviceResponse(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string DeviceSecret,
        DateTimeOffset ProvisionedAtUtc);

    private sealed record EnableDeviceRequest(Guid TenantId, Guid AdminUserId);

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

    private static string NormalizeOrExisting(string? value, string existing)
        => string.IsNullOrWhiteSpace(value)
            ? existing
            : value.Trim();
}
