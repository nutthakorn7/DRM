namespace Drm.Domain;

/// <summary>
/// Role-style persona that drives DRM UI affordances. Stored as a string
/// alongside the user; capabilities are derived (not persisted) so the
/// matrix can evolve without a migration.
/// </summary>
public enum DrmPersona
{
    /// <summary>Default — protect + invite guests, no admin, no revoke.</summary>
    Employee = 0,

    /// <summary>+ revoke own files, bulk send, view audit of own files.</summary>
    KnowledgeWorker = 1,

    /// <summary>+ tenant-wide audit view, revoke any file.</summary>
    Executive = 2,

    /// <summary>Full admin — everything.</summary>
    Admin = 3
}

public sealed record PersonaCapabilities(
    bool CanProtect,
    bool CanRevoke,
    bool CanInviteGuests,
    bool CanViewAuditLog,
    bool CanAdmin)
{
    public static PersonaCapabilities For(DrmPersona persona) => persona switch
    {
        DrmPersona.Employee        => new(true,  false, true,  false, false),
        DrmPersona.KnowledgeWorker => new(true,  true,  true,  false, false),
        DrmPersona.Executive       => new(true,  true,  true,  true,  false),
        DrmPersona.Admin           => new(true,  true,  true,  true,  true ),
        _ => new(false, false, false, false, false)
    };
}
