using Microsoft.EntityFrameworkCore;

namespace Drm.Server;

/// <summary>
/// Provider-agnostic directory→DB upsert + reconciliation. Both directory-sync providers
/// (Entra today, LDAP next) map their source into these records and call <see cref="ApplyAsync"/>,
/// so user/group/membership semantics — including offboarding — are identical and tested once.
///
/// Records are matched to existing rows by <c>ExternalId</c>. Rows with a null ExternalId are
/// manually-created and are NEVER touched by reconciliation.
/// </summary>
public sealed record DirectoryUser(string ExternalId, string Email, string DisplayName);
public sealed record DirectoryGroup(string ExternalId, string Name);
public sealed record DirectoryMembership(string GroupExternalId, string UserExternalId);

public static class DirectoryUpsert
{
    // Safety valve: if reconciliation would deactivate more than this fraction of the tenant's
    // directory-sourced users in one run (and there are more than a handful), skip deactivation
    // and warn. A transient/partial fetch must never mass-lock-out a directory.
    private const double MaxDeactivateFraction = 0.5;
    private const int MinUsersBeforeThreshold = 5;

    /// <param name="reconcileRemovals">
    /// When true, users/memberships absent from the source are deactivated/pruned (offboarding).
    /// The caller MUST pass false on any incomplete/failed fetch — only a confirmed-complete sync
    /// may reconcile. Gated per-tenant by config so it can be rolled out deliberately.
    /// </param>
    public static async Task<DirectoryUpsertResult> ApplyAsync(
        AppDbContext db,
        Guid tenantId,
        IReadOnlyList<DirectoryUser> users,
        IReadOnlyList<DirectoryGroup> groups,
        IReadOnlyList<DirectoryMembership> memberships,
        bool reconcileRemovals,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var now = DateTimeOffset.UtcNow;

        // ── Users ───────────────────────────────────────────────────────────────
        int usersUpserted = 0;
        foreach (var u in users)
        {
            if (string.IsNullOrWhiteSpace(u.ExternalId) || string.IsNullOrWhiteSpace(u.Email)) continue;
            var existing = await db.TenantUsers
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ExternalId == u.ExternalId, ct);
            if (existing is null)
            {
                db.TenantUsers.Add(new TenantUserEntity
                {
                    TenantId = tenantId,
                    UserId = Guid.NewGuid(),
                    Email = u.Email,
                    DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName,
                    ExternalId = u.ExternalId,
                    Active = true,
                    CreatedAtUtc = now,
                });
                usersUpserted++;
            }
            else
            {
                existing.Email = u.Email;
                existing.DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName;
                existing.Active = true; // re-activate someone who reappeared in the directory
            }
        }
        await db.SaveChangesAsync(ct);

        // ── Groups ──────────────────────────────────────────────────────────────
        int groupsUpserted = 0;
        foreach (var g in groups)
        {
            if (string.IsNullOrWhiteSpace(g.ExternalId)) continue;
            var existing = await db.TenantGroups
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ExternalId == g.ExternalId, ct);
            if (existing is null)
            {
                db.TenantGroups.Add(new TenantGroupEntity
                {
                    TenantId = tenantId,
                    GroupId = Guid.NewGuid(),
                    Name = string.IsNullOrWhiteSpace(g.Name) ? g.ExternalId : g.Name,
                    ExternalId = g.ExternalId,
                    CreatedAtUtc = now,
                });
                groupsUpserted++;
            }
            else
            {
                existing.Name = string.IsNullOrWhiteSpace(g.Name) ? g.ExternalId : g.Name;
            }
        }
        await db.SaveChangesAsync(ct);

        // Resolve ExternalId → internal id for directory-sourced users/groups (this tenant).
        var userByExt = await db.TenantUsers
            .Where(x => x.TenantId == tenantId && x.ExternalId != null)
            .ToDictionaryAsync(x => x.ExternalId!, x => x.UserId, ct);
        var groupByExt = await db.TenantGroups
            .Where(x => x.TenantId == tenantId && x.ExternalId != null)
            .ToDictionaryAsync(x => x.ExternalId!, x => x.GroupId, ct);

        // ── Memberships (add) ────────────────────────────────────────────────────
        int membershipsUpserted = 0;
        foreach (var m in memberships)
        {
            if (!groupByExt.TryGetValue(m.GroupExternalId, out var gid)) continue;
            if (!userByExt.TryGetValue(m.UserExternalId, out var uid)) continue;
            var exists = await db.GroupMembers
                .AnyAsync(x => x.TenantId == tenantId && x.GroupId == gid && x.UserId == uid, ct);
            if (!exists)
            {
                db.GroupMembers.Add(new GroupMemberEntity
                {
                    TenantId = tenantId, GroupId = gid, UserId = uid, CreatedAtUtc = now,
                });
                membershipsUpserted++;
            }
        }
        await db.SaveChangesAsync(ct);

        int usersDeactivated = 0, membershipsRemoved = 0;
        if (reconcileRemovals)
        {
            // Offboarding: deactivate directory-sourced users no longer in the source.
            var syncedUserExt = users.Select(u => u.ExternalId).ToHashSet(StringComparer.Ordinal);
            var dirUsers = await db.TenantUsers
                .Where(x => x.TenantId == tenantId && x.ExternalId != null && x.Active)
                .ToListAsync(ct);
            var toDeactivate = dirUsers.Where(x => !syncedUserExt.Contains(x.ExternalId!)).ToList();

            if (dirUsers.Count > MinUsersBeforeThreshold &&
                toDeactivate.Count > dirUsers.Count * MaxDeactivateFraction)
            {
                warnings.Add($"reconcile_skipped_threshold: would deactivate {toDeactivate.Count}/{dirUsers.Count} " +
                             "directory users (>50%); skipped to avoid mass lockout from a partial sync.");
            }
            else
            {
                foreach (var u in toDeactivate) { u.Active = false; usersDeactivated++; }

                // Prune memberships dropped from the source — only within groups present in THIS sync,
                // and only for directory-sourced (ExternalId-keyed) users/groups.
                var syncedMembership = memberships
                    .Select(m => (m.GroupExternalId, m.UserExternalId)).ToHashSet();
                var syncedGroupIds = groups
                    .Where(g => groupByExt.ContainsKey(g.ExternalId))
                    .Select(g => groupByExt[g.ExternalId]).ToHashSet();
                var extByUserId = userByExt.ToDictionary(kv => kv.Value, kv => kv.Key);
                var extByGroupId = groupByExt.ToDictionary(kv => kv.Value, kv => kv.Key);

                var dbMembers = await db.GroupMembers
                    .Where(x => x.TenantId == tenantId && syncedGroupIds.Contains(x.GroupId))
                    .ToListAsync(ct);
                foreach (var gm in dbMembers)
                {
                    if (!extByGroupId.TryGetValue(gm.GroupId, out var gExt)) continue;
                    if (!extByUserId.TryGetValue(gm.UserId, out var uExt)) continue; // manual member → keep
                    if (!syncedMembership.Contains((gExt, uExt)))
                    {
                        db.GroupMembers.Remove(gm);
                        membershipsRemoved++;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }

        return new DirectoryUpsertResult(
            usersUpserted, groupsUpserted, membershipsUpserted, usersDeactivated, membershipsRemoved, warnings);
    }
}

public sealed record DirectoryUpsertResult(
    int UsersUpserted,
    int GroupsUpserted,
    int MembershipsUpserted,
    int UsersDeactivated,
    int MembershipsRemoved,
    IReadOnlyList<string> Warnings);
