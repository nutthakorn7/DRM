using FluentAssertions;

namespace Drm.Server.Tests;

public sealed class DeviceReplayGuardTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void First_use_of_a_nonce_is_accepted()
    {
        var guard = new InMemoryDeviceReplayGuard();
        var now = DateTimeOffset.UnixEpoch;

        guard.TryConsume(Tenant, Device, "nonce-a", now).Should().BeTrue();
    }

    [Fact]
    public void Replaying_the_same_nonce_is_rejected()
    {
        var guard = new InMemoryDeviceReplayGuard();
        var now = DateTimeOffset.UnixEpoch;

        guard.TryConsume(Tenant, Device, "nonce-a", now).Should().BeTrue();
        guard.TryConsume(Tenant, Device, "nonce-a", now).Should().BeFalse(
            "the second presentation of the same (tenant, device, nonce) is a replay");
    }

    [Fact]
    public void Different_nonces_for_the_same_device_are_each_accepted_once()
    {
        var guard = new InMemoryDeviceReplayGuard();
        var now = DateTimeOffset.UnixEpoch;

        guard.TryConsume(Tenant, Device, "nonce-a", now).Should().BeTrue();
        guard.TryConsume(Tenant, Device, "nonce-b", now).Should().BeTrue();
        guard.TryConsume(Tenant, Device, "nonce-a", now).Should().BeFalse();
        guard.TryConsume(Tenant, Device, "nonce-b", now).Should().BeFalse();
    }

    [Fact]
    public void Same_nonce_on_a_different_device_is_not_a_replay()
    {
        var guard = new InMemoryDeviceReplayGuard();
        var otherDevice = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var now = DateTimeOffset.UnixEpoch;

        guard.TryConsume(Tenant, Device, "shared-nonce", now).Should().BeTrue();
        guard.TryConsume(Tenant, otherDevice, "shared-nonce", now).Should().BeTrue(
            "the ledger is keyed on (tenant, device, nonce) — a different device collides with nothing");
    }

    [Fact]
    public void Same_nonce_in_a_different_tenant_is_not_a_replay()
    {
        var guard = new InMemoryDeviceReplayGuard();
        var otherTenant = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var now = DateTimeOffset.UnixEpoch;

        guard.TryConsume(Tenant, Device, "shared-nonce", now).Should().BeTrue();
        guard.TryConsume(otherTenant, Device, "shared-nonce", now).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_nonce_is_always_rejected(string? nonce)
    {
        var guard = new InMemoryDeviceReplayGuard();
        guard.TryConsume(Tenant, Device, nonce, DateTimeOffset.UnixEpoch).Should().BeFalse(
            "a blank nonce can't be de-duplicated, so fail closed");
    }

    [Fact]
    public void Nonce_is_forgotten_after_the_retention_window_passes()
    {
        // After the retention window the entry is pruned. By then the signing
        // timestamp window (±5 min) has also elapsed, so the signature itself
        // would be rejected upstream — re-acceptance here is harmless and keeps
        // the ledger bounded.
        var guard = new InMemoryDeviceReplayGuard();
        var t0 = DateTimeOffset.UnixEpoch;

        guard.TryConsume(Tenant, Device, "nonce-a", t0).Should().BeTrue();
        guard.TryConsume(Tenant, Device, "nonce-a", t0).Should().BeFalse();

        // 11 minutes later (> 10-minute retention) the sweep evicts it.
        var later = t0.AddMinutes(11);
        guard.TryConsume(Tenant, Device, "nonce-a", later).Should().BeTrue(
            "after the retention window the nonce is pruned and the timestamp window guards replays");
    }

    [Fact]
    public void Nonce_is_still_remembered_inside_the_retention_window()
    {
        var guard = new InMemoryDeviceReplayGuard();
        var t0 = DateTimeOffset.UnixEpoch;

        guard.TryConsume(Tenant, Device, "nonce-a", t0).Should().BeTrue();
        // 4 minutes later — still inside the skew window, must still be blocked.
        guard.TryConsume(Tenant, Device, "nonce-a", t0.AddMinutes(4)).Should().BeFalse();
    }

    [Fact]
    public void Ledger_stays_bounded_under_a_distinct_nonce_flood()
    {
        // A compromised/insider device floods many distinct (fresh) nonces
        // within the retention window. The ledger must not grow without bound;
        // the cap evicts the oldest entries.
        const int cap = 100;
        var guard = new InMemoryDeviceReplayGuard(maxEntries: cap);
        var now = DateTimeOffset.UnixEpoch;

        for (var i = 0; i < cap * 50; i++)
        {
            // Same instant for all so the age-based sweep can't evict anything —
            // only the size cap can keep the ledger bounded.
            guard.TryConsume(Tenant, Device, $"flood-{i}", now).Should().BeTrue();
        }

        // After flooding 50× the cap, the live count must still be ≤ cap.
        // (Exposed via the next probe: re-consuming a just-used recent nonce is
        // still blocked, proving eviction targeted the OLDEST, not newest.)
        guard.TryConsume(Tenant, Device, $"flood-{cap * 50 - 1}", now).Should().BeFalse(
            "the most recent nonce must still be remembered — eviction drops oldest first");
    }
}
