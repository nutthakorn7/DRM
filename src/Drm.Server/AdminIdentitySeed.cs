namespace Drm.Server;

public static class AdminIdentitySeed
{
    public static void Run(AppDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;

        SeedRole(dbContext, AdminSystemRoles.SuperAdminId,  AdminSystemRoles.SuperAdminName,  now);
        SeedRole(dbContext, AdminSystemRoles.TenantAdminId, AdminSystemRoles.TenantAdminName, now);
        SeedRole(dbContext, AdminSystemRoles.AuditorId,     AdminSystemRoles.AuditorName,     now);
        SeedRole(dbContext, AdminSystemRoles.ReadOnlyId,    AdminSystemRoles.ReadOnlyName,    now);

        // Default SuperAdmin: the synthetic identity that shared-key auth maps to
        // until the operator issues per-admin tokens. Lets the v1.0.1 surface keep
        // working through the upgrade while every audit row still has an actor.
        var existing = dbContext.AdminUsers.FirstOrDefault(u => u.Id == AdminSystemRoles.DefaultSuperAdminUserId);
        if (existing is null)
        {
            dbContext.AdminUsers.Add(new AdminUserEntity
            {
                Id = AdminSystemRoles.DefaultSuperAdminUserId,
                Email = AdminSystemRoles.DefaultSuperAdminEmail,
                DisplayName = AdminSystemRoles.DefaultSuperAdminDisplayName,
                RoleId = AdminSystemRoles.SuperAdminId,
                Disabled = false,
                CreatedAtUtc = now,
            });
            dbContext.SaveChanges();
        }
    }

    private static void SeedRole(AppDbContext dbContext, Guid roleId, string name, DateTimeOffset now)
    {
        var existing = dbContext.AdminRoles.FirstOrDefault(r => r.Id == roleId);
        var permissions = AdminSystemRoles.PermissionsFor(roleId);

        if (existing is null)
        {
            dbContext.AdminRoles.Add(new AdminRoleEntity
            {
                Id = roleId,
                Name = name,
                PermissionsCsv = permissions,
                IsSystem = true,
                CreatedAtUtc = now,
            });
            dbContext.SaveChanges();
        }
        else if (existing.PermissionsCsv != permissions)
        {
            // Keep permission set in sync with code definitions on every boot —
            // gives operators a free, predictable upgrade path when we add new
            // permissions to a system role.
            existing.PermissionsCsv = permissions;
            existing.IsSystem = true;
            dbContext.SaveChanges();
        }
    }
}
