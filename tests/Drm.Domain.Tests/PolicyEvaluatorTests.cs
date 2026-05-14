using Drm.Domain;
using FluentAssertions;

namespace Drm.Domain.Tests;

public sealed class PolicyEvaluatorTests
{
    [Fact]
    public void Allows_view_when_user_has_view_grant_and_file_is_not_expired()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.View, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeTrue();
        decision.AllowedPermissions.Should().HaveFlag(Permission.View);
        decision.ReasonCode.Should().Be("allowed");
    }

    [Fact]
    public void Denies_when_requested_permission_is_not_granted()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddHours(1));
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.Print, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("permission_not_granted");
    }

    [Fact]
    public void Denies_when_policy_is_expired()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.View, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("expired");
    }

    [Fact]
    public void Denies_when_policy_is_revoked()
    {
        var policy = TestPolicy(Permission.View, expiresAtUtc: DateTimeOffset.UtcNow.AddHours(1)) with { Revoked = true };
        var request = new PolicyRequest(TestIds.Tenant, TestIds.File, TestIds.User, TestIds.Device, Permission.View, DateTimeOffset.UtcNow);

        var decision = PolicyEvaluator.Evaluate(policy, request);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("revoked");
    }

    private static FilePolicy TestPolicy(Permission permissions, DateTimeOffset expiresAtUtc)
        => new(
            TestIds.Tenant,
            TestIds.File,
            expiresAtUtc,
            Revoked: false,
            Grants: [new FileGrant(TestIds.User, permissions)],
            WatermarkTemplate: "{user} {time} {file}");

    private static class TestIds
    {
        public static readonly TenantId Tenant = TenantId.New();
        public static readonly ProtectedFileId File = ProtectedFileId.New();
        public static readonly UserId User = UserId.New();
        public static readonly DeviceId Device = DeviceId.New();
    }
}
