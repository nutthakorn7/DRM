namespace Drm.Agent.Core;

public sealed record AgentIdentity(Guid TenantId, Guid UserId, Guid DeviceId);

/// <summary>
/// Shape returned by GET /api/agent/discover?email=... — what the tray
/// first-run dialog learns from a work-email lookup so the user never
/// has to type a tenant or user GUID.
///
/// DefaultPolicyTemplateId may be null when the tenant has no
/// templates yet; the agent falls back to a minimum-permission policy
/// composed from DefaultExpiryDays in that case.
/// </summary>
public sealed record AgentDiscoveryResult(
    Guid TenantId,
    Guid UserId,
    string DisplayName,
    string Email,
    Guid? DefaultPolicyTemplateId,
    int DefaultExpiryDays);

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

public sealed record AgentCommand(
    Guid TenantId,
    Guid CommandId,
    Guid DeviceId,
    Guid FileId,
    string CommandType,
    string Status,
    string ReasonCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record AgentCommandCompletion(string Status, string ReasonCode);

public interface IAgentAuditUploader
{
    Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken);
}
