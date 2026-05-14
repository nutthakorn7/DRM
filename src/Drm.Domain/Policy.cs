using System.Collections.Immutable;

namespace Drm.Domain;

public sealed record FilePolicy
{
    public FilePolicy(
        TenantId TenantId,
        ProtectedFileId FileId,
        DateTimeOffset ExpiresAtUtc,
        bool Revoked,
        IEnumerable<FileGrant> Grants,
        string WatermarkTemplate)
    {
        this.TenantId = TenantId;
        this.FileId = FileId;
        this.ExpiresAtUtc = ExpiresAtUtc;
        this.Revoked = Revoked;
        this.Grants = Grants.ToImmutableArray();
        this.WatermarkTemplate = WatermarkTemplate;
    }

    public TenantId TenantId { get; init; }

    public ProtectedFileId FileId { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public bool Revoked { get; init; }

    public ImmutableArray<FileGrant> Grants { get; init; }

    public string WatermarkTemplate { get; init; }
}

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
