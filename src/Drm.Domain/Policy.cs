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
        string WatermarkTemplate,
        int? MaxOpens = null,
        int OpensUsed = 0)
    {
        this.TenantId = TenantId;
        this.FileId = FileId;
        this.ExpiresAtUtc = ExpiresAtUtc;
        this.Revoked = Revoked;
        this.Grants = Grants.ToImmutableArray();
        this.WatermarkTemplate = WatermarkTemplate;
        this.MaxOpens = MaxOpens;
        this.OpensUsed = OpensUsed;
    }

    public TenantId TenantId { get; init; }

    public ProtectedFileId FileId { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public bool Revoked { get; init; }

    public ImmutableArray<FileGrant> Grants { get; init; }

    public string WatermarkTemplate { get; init; }

    /// <summary>
    /// Maximum number of times this file may be opened by any single user.
    /// <c>null</c> means unlimited. When the per-user open count reaches this
    /// value, further access is denied with <c>opens_exhausted</c>.
    /// </summary>
    public int? MaxOpens { get; init; }

    /// <summary>
    /// Opens already consumed by the requesting user. Compared against
    /// <see cref="MaxOpens"/> to determine whether another open is allowed.
    /// </summary>
    public int OpensUsed { get; init; }
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
