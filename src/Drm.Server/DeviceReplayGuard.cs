using System.Collections.Concurrent;

namespace Drm.Server;

/// <summary>
/// Replay protection for signed device requests (PR #50 hardening).
///
/// Every <see cref="Drm.Domain.DeviceRequestSignature"/> already carries a
/// random per-request nonce that is folded into the HMAC, but nothing on the
/// server ever recorded which nonces had been seen — so a captured signed
/// request could be replayed verbatim inside the ±5-minute clock-skew window.
/// This guard closes that window: the first time a (tenant, device, nonce)
/// triple is presented it is accepted and remembered; any subsequent
/// presentation of the same triple is rejected.
///
/// Storage is in-memory and TTL-pruned. Production runs a single server
/// instance (see docs/demo CISO notes), and a nonce only needs to be
/// remembered for the duration of the clock-skew window, so an in-memory
/// ledger is sufficient and avoids a database migration. If the deployment
/// ever scales to multiple server instances, this must move to a shared
/// store (Redis / a Postgres table with a unique index on
/// (TenantId, DeviceId, Nonce)) — see the PR follow-up checklist.
/// </summary>
public interface IDeviceReplayGuard
{
    /// <summary>
    /// Returns <c>true</c> if this (tenant, device, nonce) triple has not been
    /// seen before (and records it); <c>false</c> if it is a replay. A blank
    /// nonce is always treated as a replay/failure — signature verification
    /// already requires a non-empty nonce, so reaching here with a blank one
    /// means something is wrong and we fail closed.
    /// </summary>
    bool TryConsume(Guid tenantId, Guid deviceId, string? nonce, DateTimeOffset nowUtc);
}

public sealed class InMemoryDeviceReplayGuard : IDeviceReplayGuard
{
    // Twice the signing clock-skew window (5 min) so a nonce is remembered for
    // at least as long as a signature for it could still pass the timestamp
    // check. After this, the timestamp window itself rejects the replay, so the
    // entry is safe to forget.
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    // Hard ceiling on the ledger so a compromised/insider device that floods
    // many distinct-nonce signed requests can't grow memory without bound
    // (only reachable after a valid signature, but still worth capping). When
    // exceeded we evict the OLDEST entries — those are closest to expiry, so
    // eviction barely widens the replay window, and the ±5-min timestamp check
    // still guards anything evicted early.
    private readonly int maxEntries;

    private readonly ConcurrentDictionary<string, DateTimeOffset> seen = new(StringComparer.Ordinal);
    private long lastSweepTicks;

    public InMemoryDeviceReplayGuard(int maxEntries = 200_000)
    {
        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        this.maxEntries = maxEntries;
    }

    public bool TryConsume(Guid tenantId, Guid deviceId, string? nonce, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(nonce))
        {
            return false;
        }

        Sweep(nowUtc);

        var key = $"{tenantId:N}:{deviceId:N}:{nonce.Trim()}";
        // TryAdd is atomic: it returns false if the key is already present,
        // which is exactly our replay signal. No lock needed.
        var added = seen.TryAdd(key, nowUtc);

        if (added && seen.Count > maxEntries)
        {
            EvictOldest();
        }

        return added;
    }

    private void EvictOldest()
    {
        // Drop down to 90% of the cap so this runs rarely (not on every insert
        // while at the ceiling). Over-eviction under concurrency is harmless —
        // it only trims a few extra near-expiry nonces.
        var target = (int)(maxEntries * 0.9);
        var overage = seen.Count - target;
        if (overage <= 0)
        {
            return;
        }

        foreach (var key in seen
            .OrderBy(entry => entry.Value)
            .Take(overage)
            .Select(entry => entry.Key)
            .ToList())
        {
            seen.TryRemove(key, out _);
        }
    }

    private void Sweep(DateTimeOffset nowUtc)
    {
        var nowTicks = nowUtc.UtcTicks;
        var last = Interlocked.Read(ref lastSweepTicks);
        if (nowTicks - last < SweepInterval.Ticks)
        {
            return;
        }

        // Only one thread wins the sweep; others skip it this tick.
        if (Interlocked.CompareExchange(ref lastSweepTicks, nowTicks, last) != last)
        {
            return;
        }

        var cutoff = nowUtc - RetentionWindow;
        foreach (var entry in seen)
        {
            if (entry.Value < cutoff)
            {
                seen.TryRemove(entry.Key, out _);
            }
        }
    }
}
