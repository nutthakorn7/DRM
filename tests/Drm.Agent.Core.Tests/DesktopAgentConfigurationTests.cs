using Drm.Agent.Core;
using FluentAssertions;

namespace Drm.Agent.Core.Tests;

public sealed class DesktopAgentConfigurationTests
{
    [Fact]
    public void FromSources_prefers_environment_then_current_user_then_local_machine()
    {
        var envTenantId = Guid.NewGuid();
        var currentUserDeviceId = Guid.NewGuid();
        var localMachineUserId = Guid.NewGuid();

        var config = DesktopAgentConfiguration.FromSources(
            name => name switch
            {
                "DrmAgent__ServerUrl" => "https://env.example",
                "DrmAgent__TenantId" => envTenantId.ToString(),
                _ => null
            },
            name => name switch
            {
                "ClientApiKey" => "drm_client_current_user",
                "DeviceId" => currentUserDeviceId.ToString(),
                _ => null
            },
            name => name switch
            {
                "ServerUrl" => "https://machine.example",
                "ClientApiKey" => "drm_client_machine",
                "UserId" => localMachineUserId.ToString(),
                "DeviceId" => Guid.NewGuid().ToString(),
                "DeviceSecret" => "machine-device-secret",
                _ => null
            },
            includeDeviceSecret: true);

        config.ServerUrl.Should().Be(new Uri("https://env.example"));
        config.ClientApiKey.Should().Be("drm_client_current_user");
        config.TenantId.Should().Be(envTenantId);
        config.UserId.Should().Be(localMachineUserId);
        config.DeviceId.Should().Be(currentUserDeviceId);
        config.DeviceSecret.Should().Be("machine-device-secret");
    }

    [Fact]
    public void TryCreateIdentity_combines_machine_device_with_cached_user_identity()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var cache = new AgentIdentityCacheEntry(
            tenantId,
            userId,
            "alice@corp.example",
            "Alice",
            new Uri("https://drm.example"),
            DefaultPolicyTemplateId: null,
            DefaultExpiryDays: 30,
            SavedAtUtc: DateTimeOffset.UtcNow);

        var config = new DesktopAgentConfiguration(
            ServerUrl: null,
            ClientApiKey: "drm_client_machine",
            TenantId: null,
            UserId: null,
            DeviceId: deviceId,
            DeviceSecret: "machine-device-secret");

        config.TryCreateIdentity(cache).Should().Be(new AgentIdentity(tenantId, userId, deviceId, "machine-device-secret"));
    }
}
