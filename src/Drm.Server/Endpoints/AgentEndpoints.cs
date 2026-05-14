using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class AgentEndpoints
{
    private static readonly string[] AllowedAuditEventPrefixes =
    [
        "agent_",
        "file_",
        "access_",
        "print_",
        "export_",
        "copy_"
    ];

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/agent");

        group.MapPost("/devices/register", RegisterDeviceAsync);
        group.MapPost("/devices/{deviceId:guid}/heartbeat", RecordHeartbeatAsync);
        group.MapPost("/audit", IngestAuditAsync);

        return endpoints;
    }

    private static async Task<Results<Created<RegisterDeviceResponse>, Ok<RegisterDeviceResponse>, BadRequest<ErrorResponse>>> RegisterDeviceAsync(
        RegisterDeviceRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (IsBlank(request.Hostname) || IsBlank(request.OperatingSystem) || IsBlank(request.AgentVersion))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_device_metadata"));
        }

        var now = DateTimeOffset.UtcNow;
        var device = await dbContext.AgentDevices
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.DeviceId == request.DeviceId,
                cancellationToken);

        if (device is not null)
        {
            device.UserId = request.UserId;
            device.Hostname = request.Hostname.Trim();
            device.OperatingSystem = request.OperatingSystem.Trim();
            device.AgentVersion = request.AgentVersion.Trim();
            device.Status = "registered";
            device.UpdatedAtUtc = now;

            await dbContext.SaveChangesAsync(cancellationToken);

            return TypedResults.Ok(ToRegisterResponse(device));
        }

        device = new AgentDeviceEntity
        {
            TenantId = request.TenantId,
            DeviceId = request.DeviceId,
            UserId = request.UserId,
            Hostname = request.Hostname.Trim(),
            OperatingSystem = request.OperatingSystem.Trim(),
            AgentVersion = request.AgentVersion.Trim(),
            Status = "registered",
            RegisteredAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.AgentDevices.Add(device);
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = device.TenantId,
            UserId = device.UserId,
            EventType = "agent_registered",
            ReasonCode = "registered",
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/agent/devices/{device.DeviceId}", ToRegisterResponse(device));
    }

    private static async Task<Results<Ok<HeartbeatResponse>, NotFound, BadRequest<ErrorResponse>>> RecordHeartbeatAsync(
        Guid deviceId,
        HeartbeatRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (IsBlank(request.Status) || IsBlank(request.AgentVersion))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_heartbeat"));
        }

        var device = await dbContext.AgentDevices
            .SingleOrDefaultAsync(candidate =>
                candidate.TenantId == request.TenantId &&
                candidate.DeviceId == deviceId,
                cancellationToken);

        if (device is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        device.UserId = request.UserId;
        device.Status = request.Status.Trim();
        device.AgentVersion = request.AgentVersion.Trim();
        device.LastHeartbeatAtUtc = now;
        device.UpdatedAtUtc = now;

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = device.TenantId,
            UserId = device.UserId,
            EventType = "agent_heartbeat",
            ReasonCode = device.Status,
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new HeartbeatResponse(device.DeviceId, device.Status, now));
    }

    private static async Task<Results<Accepted, BadRequest<ErrorResponse>>> IngestAuditAsync(
        AgentAuditRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedAuditEventType(request.EventType) || IsBlank(request.ReasonCode))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_audit_event"));
        }

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            FileId = request.FileId,
            UserId = request.UserId,
            EventType = request.EventType.Trim(),
            ReasonCode = request.ReasonCode.Trim(),
            CreatedAtUtc = request.CreatedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Accepted("/api/agent/audit");
    }

    private static RegisterDeviceResponse ToRegisterResponse(AgentDeviceEntity device)
        => new(
            device.TenantId,
            device.UserId,
            device.DeviceId,
            device.Hostname,
            device.OperatingSystem,
            device.AgentVersion,
            device.Status,
            device.RegisteredAtUtc,
            device.LastHeartbeatAtUtc);

    private static bool IsAllowedAuditEventType(string eventType)
    {
        if (IsBlank(eventType))
        {
            return false;
        }

        return AllowedAuditEventPrefixes.Any(prefix =>
            eventType.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsBlank(string value)
        => string.IsNullOrWhiteSpace(value);

    private sealed record RegisterDeviceRequest(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string Hostname,
        string OperatingSystem,
        string AgentVersion);

    private sealed record RegisterDeviceResponse(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string Hostname,
        string OperatingSystem,
        string AgentVersion,
        string Status,
        DateTimeOffset RegisteredAtUtc,
        DateTimeOffset? LastHeartbeatAtUtc);

    private sealed record HeartbeatRequest(
        Guid TenantId,
        Guid UserId,
        string Status,
        string AgentVersion);

    private sealed record HeartbeatResponse(Guid DeviceId, string Status, DateTimeOffset LastHeartbeatAtUtc);

    private sealed record AgentAuditRequest(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        Guid? FileId,
        string EventType,
        string ReasonCode,
        DateTimeOffset CreatedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
