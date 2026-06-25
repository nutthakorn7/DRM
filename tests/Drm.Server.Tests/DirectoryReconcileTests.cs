using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// Reconciliation in the shared <see cref="DirectoryUpsert"/> — the offboarding behavior both
/// directory-sync providers inherit. Exercised directly (pure DB logic; no Graph/LDAP needed).
/// </summary>
public sealed class DirectoryReconcileTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-dirsync-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public DirectoryReconcileTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
            b.UseSetting("Drm:Mode", "OnPrem");
            b.UseSetting("Drm:KeyWrapping:MasterKeyBase64",
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(v => (byte)v).ToArray()));
        });
    }

    [Fact]
    public async Task Reconcile_deactivates_directory_users_gone_from_source()
    {
        var t = Guid.NewGuid();
        await Seed(t, ("alice", "A", true), ("bob", "B", true));

        var r = await Apply(t, users: new[] { U("alice", "A") }, reconcile: true);

        r.UsersDeactivated.Should().Be(1);
        (await Active(t, "B")).Should().BeFalse("bob vanished from the source");
        (await Active(t, "A")).Should().BeTrue("alice is still in the source");
    }

    [Fact]
    public async Task Reconcile_off_by_default_never_deactivates()
    {
        var t = Guid.NewGuid();
        await Seed(t, ("alice", "A", true), ("bob", "B", true));

        var r = await Apply(t, users: new[] { U("alice", "A") }, reconcile: false);

        r.UsersDeactivated.Should().Be(0);
        (await Active(t, "B")).Should().BeTrue("reconcile is gated off — no offboarding without the flag");
    }

    [Fact]
    public async Task Reconcile_never_touches_manually_created_users()
    {
        var t = Guid.NewGuid();
        await Seed(t, ("alice", "A", true));
        await SeedManual(t, "manual@x.com"); // ExternalId == null

        await Apply(t, users: new[] { U("alice", "A") }, reconcile: true);

        (await ActiveByEmail(t, "manual@x.com")).Should().BeTrue("manual (non-directory) users are never reconciled");
    }

    [Fact]
    public async Task Reappearing_user_is_reactivated()
    {
        var t = Guid.NewGuid();
        await Seed(t, ("bob", "B", false)); // previously deactivated

        await Apply(t, users: new[] { U("bob", "B") }, reconcile: true);

        (await Active(t, "B")).Should().BeTrue("a user back in the source is re-activated");
    }

    [Fact]
    public async Task Reconcile_prunes_memberships_dropped_from_source()
    {
        var t = Guid.NewGuid();
        await Seed(t, ("alice", "A", true), ("bob", "B", true));
        // First sync: both in group G.
        await Apply(t,
            users: new[] { U("alice", "A"), U("bob", "B") },
            groups: new[] { new DirectoryGroup("G", "Group") },
            memberships: new[] { new DirectoryMembership("G", "A"), new DirectoryMembership("G", "B") },
            reconcile: true);

        // Second sync: bob dropped from G.
        var r = await Apply(t,
            users: new[] { U("alice", "A"), U("bob", "B") },
            groups: new[] { new DirectoryGroup("G", "Group") },
            memberships: new[] { new DirectoryMembership("G", "A") },
            reconcile: true);

        r.MembershipsRemoved.Should().Be(1);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gid = await db.TenantGroups.Where(g => g.TenantId == t && g.ExternalId == "G").Select(g => g.GroupId).SingleAsync();
        var aliceId = await db.TenantUsers.Where(u => u.TenantId == t && u.ExternalId == "A").Select(u => u.UserId).SingleAsync();
        var bobId = await db.TenantUsers.Where(u => u.TenantId == t && u.ExternalId == "B").Select(u => u.UserId).SingleAsync();
        (await db.GroupMembers.AnyAsync(m => m.TenantId == t && m.GroupId == gid && m.UserId == aliceId)).Should().BeTrue();
        (await db.GroupMembers.AnyAsync(m => m.TenantId == t && m.GroupId == gid && m.UserId == bobId)).Should().BeFalse("bob was dropped from the source group");
    }

    [Fact]
    public async Task Reconcile_refuses_mass_deactivation_above_threshold()
    {
        var t = Guid.NewGuid();
        // 10 directory users; a partial fetch returning only 1 would otherwise deactivate 9 (>50%).
        await Seed(t, Enumerable.Range(0, 10).Select(i => ($"u{i}", $"E{i}", true)).ToArray());

        var r = await Apply(t, users: new[] { U("u0", "E0") }, reconcile: true);

        r.UsersDeactivated.Should().Be(0, "the >50% mass-deactivation guard tripped");
        r.Warnings.Should().Contain(w => w.Contains("reconcile_skipped_threshold"));
        (await Active(t, "E5")).Should().BeTrue("nobody was deactivated when the guard tripped");
    }

    // ── helpers ──
    private static DirectoryUser U(string name, string ext) => new(ext, $"{name}@x.com", name);

    private async Task Seed(Guid tenantId, params (string Name, string Ext, bool Active)[] users)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (name, ext, active) in users)
            db.TenantUsers.Add(new TenantUserEntity
            {
                TenantId = tenantId, UserId = Guid.NewGuid(), Email = $"{name}@x.com",
                DisplayName = name, ExternalId = ext, Active = active, CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync();
    }

    private async Task SeedManual(Guid tenantId, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = tenantId, UserId = Guid.NewGuid(), Email = email, DisplayName = email,
            ExternalId = null, Active = true, CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<DirectoryUpsertResult> Apply(
        Guid tenantId, DirectoryUser[] users, bool reconcile,
        DirectoryGroup[]? groups = null, DirectoryMembership[]? memberships = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await DirectoryUpsert.ApplyAsync(
            db, tenantId, users, groups ?? Array.Empty<DirectoryGroup>(),
            memberships ?? Array.Empty<DirectoryMembership>(), reconcile, CancellationToken.None);
    }

    private async Task<bool> Active(Guid tenantId, string ext)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TenantUsers.Where(u => u.TenantId == tenantId && u.ExternalId == ext).Select(u => u.Active).SingleAsync();
    }

    private async Task<bool> ActiveByEmail(Guid tenantId, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TenantUsers.Where(u => u.TenantId == tenantId && u.Email == email).Select(u => u.Active).SingleAsync();
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }
}
