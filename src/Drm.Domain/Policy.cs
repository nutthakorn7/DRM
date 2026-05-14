namespace Drm.Domain;

public sealed record FilePolicy(
    TenantId TenantId,
    ProtectedFileId FileId,
    DateTimeOffset ExpiresAtUtc,
    bool Revoked,
    IReadOnlyCollection<FileGrant> Grants,
    string WatermarkTemplate);

public sealed record FileGrant(
    UserId UserId,
    Permission Permissions);

public sealed record PolicyRequest(
    TenantId TenantId,
    ProtectedFileId FileId,
    UserId UserId,
    DeviceId DeviceId,
    Permission RequestedPermission,
    DateTimeOffset AtUtc);
