namespace Drm.Agent.Core;

public sealed record AgentIdentity(Guid TenantId, Guid UserId, Guid DeviceId);

public sealed record AgentDeviceRegistration(
    Guid TenantId,
    Guid UserId,
    Guid DeviceId,
    string Hostname,
    string OperatingSystem,
    string AgentVersion,
    string Status,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc);

public sealed record AgentHeartbeat(Guid DeviceId, string Status, DateTimeOffset LastHeartbeatAtUtc);

public sealed record AgentAuditRecord(
    Guid TenantId,
    Guid UserId,
    Guid DeviceId,
    Guid? FileId,
    string EventType,
    string ReasonCode,
    DateTimeOffset CreatedAtUtc);

public interface IAgentAuditUploader
{
    Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken);
}
