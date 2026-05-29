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

    private readonly ConcurrentDictionary<string, DateTimeOffset> seen = new(StringComparer.Ordinal);
    private long lastSweepTicks;

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
        return seen.TryAdd(key, nowUtc);
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
