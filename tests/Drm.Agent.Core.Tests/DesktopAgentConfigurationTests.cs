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
    public void DeviceSecret_is_read_from_the_secure_machine_key_when_provided()
    {
        // PR #51 hardening: the secret moved to an ACL'd \Secure subkey. When a
        // secure-machine source is supplied it must win over the legacy root
        // location (so a stale value left at the old key can't shadow it).
        var config = DesktopAgentConfiguration.FromSources(
            environment: _ => null,
            currentUserRegistry: _ => null,
            localMachineRegistry: name => name == "DeviceSecret" ? "legacy-root-secret" : null,
            includeDeviceSecret: true,
            secureLocalMachineRegistry: name => name == "DeviceSecret" ? "secure-key-secret" : null);

        config.DeviceSecret.Should().Be("secure-key-secret");
    }

    [Fact]
    public void DeviceSecret_falls_back_to_legacy_root_key_when_secure_key_is_empty()
    {
        // Devices provisioned by a pre-hardening MSI only have the secret at the
        // legacy root location — they must keep working after upgrade.
        var config = DesktopAgentConfiguration.FromSources(
            environment: _ => null,
            currentUserRegistry: _ => null,
            localMachineRegistry: name => name == "DeviceSecret" ? "legacy-root-secret" : null,
            includeDeviceSecret: true,
            secureLocalMachineRegistry: _ => null);

        config.DeviceSecret.Should().Be("legacy-root-secret");
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
