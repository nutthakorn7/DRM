using System.Security.Cryptography;
using System.Text;

namespace Drm.Server;

public sealed class AdminUserEntity
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public Guid RoleId { get; set; }

    public bool Disabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    // Null = global (all tenants). Non-null = scoped to one tenant only.
    public Guid? TenantScope { get; set; }
}

public sealed class AdminRoleEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PermissionsCsv { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class AdminApiTokenEntity
{
    public Guid Id { get; set; }

    public Guid AdminUserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool Revoked { get; set; }
}

public static class AdminPermissions
{
    public const string Wildcard = "*";

    public const string AdminsRead = "admins:read";
    public const string AdminsWrite = "admins:write";
    public const string RolesRead = "roles:read";
    public const string RolesWrite = "roles:write";

    public const string TenantsRead = "tenants:read";
    public const string TenantsWrite = "tenants:write";
    public const string TenantsDelete = "tenants:delete";

    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    public const string UsersDelete = "users:delete";

    public const string GroupsRead = "groups:read";
    public const string GroupsWrite = "groups:write";

    public const string FilesRead = "files:read";
    public const string FilesRevoke = "files:revoke";
    public const string FilesZip = "files:zip";

    public const string PoliciesRead = "policies:read";
    public const string PoliciesWrite = "policies:write";

    public const string AuditRead = "audit:read";
    public const string AuditExport = "audit:export";

    public const string IntegrationsRead = "integrations:read";
    public const string IntegrationsWrite = "integrations:write";

    public const string SettingsRead = "settings:read";
    public const string SettingsWrite = "settings:write";

    public static readonly IReadOnlyList<string> All =
    [
        AdminsRead, AdminsWrite, RolesRead, RolesWrite,
        TenantsRead, TenantsWrite, TenantsDelete,
        UsersRead, UsersWrite, UsersDelete,
        GroupsRead, GroupsWrite,
        FilesRead, FilesRevoke, FilesZip,
        PoliciesRead, PoliciesWrite,
        AuditRead, AuditExport,
        IntegrationsRead, IntegrationsWrite,
        SettingsRead, SettingsWrite,
    ];
}

public static class AdminSystemRoles
{
    // Stable, hardcoded UUIDs let the seed step be idempotent across restarts.
    public static readonly Guid SuperAdminId  = new("11111111-aaaa-aaaa-aaaa-111111111111");
    public static readonly Guid TenantAdminId = new("22222222-aaaa-aaaa-aaaa-222222222222");
    public static readonly Guid AuditorId     = new("33333333-aaaa-aaaa-aaaa-333333333333");
    public static readonly Guid ReadOnlyId    = new("44444444-aaaa-aaaa-aaaa-444444444444");

    public const string SuperAdminName  = "SuperAdmin";
    public const string TenantAdminName = "TenantAdmin";
    public const string AuditorName     = "Auditor";
    public const string ReadOnlyName    = "ReadOnly";

    // The bootstrap SuperAdmin that the shared admin key authenticates as
    // until per-admin tokens are issued. Audit events from shared-key auth
    // attribute to this id so v1.0.1 deployments keep a coherent audit trail
    // through the migration.
    public static readonly Guid DefaultSuperAdminUserId = new("00000000-aaaa-aaaa-aaaa-000000000001");
    public const string DefaultSuperAdminEmail = "default-admin@drm.local";
    public const string DefaultSuperAdminDisplayName = "Default SuperAdmin (shared key)";

    public static string PermissionsFor(Guid roleId)
    {
        if (roleId == SuperAdminId) return AdminPermissions.Wildcard;
        if (roleId == TenantAdminId)
        {
            // TenantAdmin: everything except admin/role management.
            return string.Join(",", AdminPermissions.All
                .Where(p => !p.StartsWith("admins:", StringComparison.Ordinal)
                         && p != AdminPermissions.RolesWrite));
        }
        if (roleId == AuditorId)
        {
            return string.Join(",", AdminPermissions.All
                .Where(p => p.EndsWith(":read", StringComparison.Ordinal))
                .Append(AdminPermissions.AuditExport));
        }
        if (roleId == ReadOnlyId)
        {
            return string.Join(",", AdminPermissions.All
                .Where(p => p.EndsWith(":read", StringComparison.Ordinal)));
        }
        return string.Empty;
    }
}

public sealed record AdminIdentity(
    Guid AdminUserId,
    Guid RoleId,
    string DisplayName,
    string Email,
    IReadOnlySet<string> Permissions,
    bool IsSharedKeyFallback,
    Guid? TenantScope = null)
{
    public bool Has(string permission)
    {
        if (Permissions.Contains(AdminPermissions.Wildcard)) return true;
        return Permissions.Contains(permission);
    }

    public bool CanAccessTenant(Guid tenantId)
        => TenantScope is null || TenantScope.Value == tenantId;
}

public static class AdminTokenCrypto
{
    public const string TokenPrefix = "drm_admin_";

    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return TokenPrefix + Base64UrlEncode(bytes);
    }

    public static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public static bool TokensMatch(string storedHash, string submittedToken)
    {
        var submittedHash = HashToken(submittedToken);
        var storedBytes = Convert.FromHexString(storedHash);
        var submittedBytes = Convert.FromHexString(submittedHash);
        return storedBytes.Length == submittedBytes.Length
            && CryptographicOperations.FixedTimeEquals(storedBytes, submittedBytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class AdminIdentityContext
{
    public const string ContextKey = "DrmAdminIdentity";

    public static AdminIdentity? Current(HttpContext context)
        => context.Items.TryGetValue(ContextKey, out var value) ? value as AdminIdentity : null;

    public static bool TryRequirePermission(HttpContext context, string permission, out IResult? failure)
    {
        var identity = Current(context);
        if (identity is null)
        {
            failure = Results.Json(new { reasonCode = "admin_identity_missing" }, statusCode: 401);
            return false;
        }
        if (!identity.Has(permission))
        {
            failure = Results.Json(new { reasonCode = "permission_denied", required = permission }, statusCode: 403);
            return false;
        }
        failure = null;
        return true;
    }

    public static bool TryRequirePermissionForTenant(HttpContext context, string permission, Guid tenantId, out IResult? failure)
    {
        if (!TryRequirePermission(context, permission, out failure))
            return false;

        var identity = Current(context)!;
        if (!identity.CanAccessTenant(tenantId))
        {
            failure = Results.Json(new { reasonCode = "tenant_scope_denied", tenantId }, statusCode: 403);
            return false;
        }
        failure = null;
        return true;
    }
}
