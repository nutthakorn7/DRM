using Drm.Domain;
using FluentAssertions;

namespace Drm.Domain.Tests;

public sealed class PolicyEvaluatorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 5, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Allows_view_when_user_has_view_grant_and_file_is_not_expired()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1));
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.AllowedPermissions.Should().HaveFlag(Permission.View);
        decision.ReasonCode.Should().Be("allowed");
    }

    [Fact]
    public void Denies_when_requested_permission_is_not_granted()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1));
        var request = TestRequest(Permission.Print);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("permission_not_granted");
    }

    [Fact]
    public void Denies_when_policy_is_expired()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddMinutes(-1));
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("expired");
    }

    [Fact]
    public void Denies_when_policy_is_revoked()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1)) with { Revoked = true };
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("revoked");
    }

    [Fact]
    public void Denies_when_tenant_does_not_match()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1));
        var request = TestRequest(Permission.View) with { TenantId = TenantId.New() };

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("tenant_mismatch");
        decision.AllowedPermissions.Should().Be(Permission.None);
        decision.WatermarkTemplate.Should().BeNull();
    }

    [Fact]
    public void Denies_when_file_does_not_match()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1));
        var request = TestRequest(Permission.View) with { FileId = ProtectedFileId.New() };

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("file_mismatch");
    }

    [Fact]
    public void Denies_when_user_has_no_grant()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1));
        var request = TestRequest(Permission.View) with { UserId = UserId.New() };

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("no_grant");
    }

    [Fact]
    public void Allows_requested_permission_when_grant_has_combined_flags()
    {
        var policy = TestPolicy(Permission.View | Permission.Print, expiresAtUtc: NowUtc.AddHours(1));
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.AllowedPermissions.Should().HaveFlag(Permission.View);
        decision.AllowedPermissions.Should().HaveFlag(Permission.Print);
    }

    [Fact]
    public void Allows_when_request_time_matches_expiry_boundary()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc);
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.ReasonCode.Should().Be("allowed");
    }

    [Fact]
    public void Allows_when_max_opens_is_null_regardless_of_opens_used()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1))
            with { MaxOpens = null, OpensUsed = 9_999 };
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.ReasonCode.Should().Be("allowed");
        decision.OpensRemaining.Should().BeNull("unlimited policies should not report a remaining count");
    }

    [Fact]
    public void Reports_opens_remaining_after_this_access_when_max_opens_is_set()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1))
            with { MaxOpens = 5, OpensUsed = 2 };
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        // 5 limit minus 2 already used minus 1 consumed by this access = 2.
        decision.OpensRemaining.Should().Be(2);
    }

    [Fact]
    public void Allows_the_final_open_when_remaining_equals_one()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1))
            with { MaxOpens = 3, OpensUsed = 2 };
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.OpensRemaining.Should().Be(0);
    }

    [Fact]
    public void Denies_with_opens_exhausted_when_user_has_consumed_all_opens()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1))
            with { MaxOpens = 3, OpensUsed = 3 };
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("opens_exhausted");
        decision.OpensRemaining.Should().Be(0);
    }

    [Fact]
    public void Denies_with_opens_exhausted_even_when_opens_used_exceeds_max()
    {
        // Defensive: if MaxOpens was lowered after the fact and the user is
        // now past the limit, still deny rather than report a negative count.
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1))
            with { MaxOpens = 2, OpensUsed = 5 };
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("opens_exhausted");
        decision.OpensRemaining.Should().Be(0);
    }

    [Fact]
    public void Opens_check_runs_after_permission_check_not_before()
    {
        // Permission denial must take precedence over opens_exhausted so that
        // a user without the right grant never burns an open from their tally.
        var policy = TestPolicy(Permission.View, expiresAtUtc: NowUtc.AddHours(1))
            with { MaxOpens = 0, OpensUsed = 0 };
        var request = TestRequest(Permission.Print);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("permission_not_granted");
    }

    [Fact]
    public void Grants_are_not_changed_by_mutating_source_collection_after_policy_construction()
    {
        var grants = new List<FileGrant> { new(TestIds.User, Permission.View) };
        var policy = new FilePolicy(
            TestIds.Tenant,
            TestIds.File,
            NowUtc.AddHours(1),
            Revoked: false,
            Grants: grants,
            WatermarkTemplate: "{user} {time} {file}");
        grants.Clear();
        var request = TestRequest(Permission.View);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.ReasonCode.Should().Be("allowed");
    }

    private static FilePolicy TestPolicy(Permission permissions, DateTimeOffset expiresAtUtc)
        => new(
            TestIds.Tenant,
            TestIds.File,
            expiresAtUtc,
            Revoked: false,
            Grants: [new FileGrant(TestIds.User, permissions)],
            WatermarkTemplate: "{user} {time} {file}");

    private static PolicyRequest TestRequest(Permission permission)
        => new(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, permission, NowUtc);

    private static class TestIds
    {
        public static readonly TenantId Tenant = TenantId.New();
        public static readonly ProtectedFileId File = ProtectedFileId.New();
        public static readonly UserId User = UserId.New();
        public static readonly DeviceId Device = DeviceId.New();
    }
}
