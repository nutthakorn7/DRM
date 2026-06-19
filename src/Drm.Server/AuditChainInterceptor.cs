using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Drm.Server;

/// <summary>
/// Guarantees every <see cref="AuditEventEntity"/> insert gets a tamper-evident
/// <see cref="AuditChainEntity"/> row. Registered as a single EF interceptor so no
/// audit-write path (there are 50+) can forget to chain.
///
/// Flow: during SavingChanges the inserted audit events are captured (their Ids do not
/// exist yet); after the save (Ids assigned) one chain row is appended + persisted per
/// event, in (TenantId, Id) order, saving after each so the next prevHash is visible.
/// An AsyncLocal re-entry guard plus pull-before-process makes the nested save a no-op.
/// </summary>
public sealed class AuditChainInterceptor : SaveChangesInterceptor
{
    private readonly string _hmacKeyHex;
    private static readonly AsyncLocal<bool> _reentry = new();
    // Keyed by the live DbContext instance → safe even though the interceptor is shared.
    private readonly ConditionalWeakTable<DbContext, List<AuditEventEntity>> _pending = new();

    public AuditChainInterceptor(string hmacKeyHex) => _hmacKeyHex = hmacKeyHex;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AppendPendingAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        await AppendPendingAsync(eventData.Context, ct);
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    private void Capture(DbContext? context)
    {
        if (context is null || _reentry.Value)
            return;

        var added = context.ChangeTracker.Entries<AuditEventEntity>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (added.Count > 0)
            _pending.AddOrUpdate(context, added);
    }

    private async Task AppendPendingAsync(DbContext? context, CancellationToken ct)
    {
        if (context is not AppDbContext db || _reentry.Value)
            return;
        if (!_pending.TryGetValue(db, out var events))
            return;
        _pending.Remove(db); // pull before processing so a nested save can't re-enter on it

        _reentry.Value = true;
        try
        {
            foreach (var evt in events.OrderBy(e => e.TenantId).ThenBy(e => e.Id))
            {
                await AuditChainService.AppendAsync(db, evt, _hmacKeyHex, ct);
                await db.SaveChangesAsync(ct); // persist so the next prevHash lookup sees it
            }
        }
        finally
        {
            _reentry.Value = false;
        }
    }
}
