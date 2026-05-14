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
