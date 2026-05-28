using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Drm.Agent.Core;

public sealed record DesktopAgentConfiguration(
    Uri? ServerUrl,
    string ClientApiKey,
    Guid? TenantId,
    Guid? UserId,
    Guid? DeviceId,
    string DeviceSecret = "")
{
    public const string RegistryRootKey = @"SOFTWARE\zcrDRM";

    public static DesktopAgentConfiguration Load(bool includeDeviceSecret = false)
    {
        if (OperatingSystem.IsWindows())
        {
            return LoadWithWindowsRegistry(includeDeviceSecret);
        }

        return FromSources(
            ReadEnvironmentValue,
            _ => null,
            _ => null,
            includeDeviceSecret);
    }

    [SupportedOSPlatform("windows")]
    private static DesktopAgentConfiguration LoadWithWindowsRegistry(bool includeDeviceSecret)
        => FromSources(
            ReadEnvironmentValue,
            name => ReadRegistryValue(RegistryHive.CurrentUser, name),
            name => ReadRegistryValue(RegistryHive.LocalMachine, name),
            includeDeviceSecret);

    public static DesktopAgentConfiguration FromSources(
        Func<string, string?> environment,
        Func<string, string?> currentUserRegistry,
        Func<string, string?> localMachineRegistry,
        bool includeDeviceSecret = false)
    {
        var serverUrlText = FirstNonBlank(
            environment("DrmAgent__ServerUrl"),
            environment("DRM_AGENT_SERVER_URL"),
            currentUserRegistry("ServerUrl"),
            localMachineRegistry("ServerUrl"));

        var clientApiKey = FirstNonBlank(
            environment("DrmAgent__ClientApiKey"),
            environment("DRM_AGENT_CLIENT_API_KEY"),
            currentUserRegistry("ClientApiKey"),
            localMachineRegistry("ClientApiKey")) ?? string.Empty;

        var deviceSecret = includeDeviceSecret
            ? FirstNonBlank(
                environment("DrmAgent__DeviceSecret"),
                environment("DRM_AGENT_DEVICE_SECRET"),
                currentUserRegistry("DeviceSecret"),
                localMachineRegistry("DeviceSecret")) ?? string.Empty
            : string.Empty;

        return new DesktopAgentConfiguration(
            TryParseUri(serverUrlText),
            clientApiKey,
            FirstGuid(
                environment("DrmAgent__TenantId"),
                environment("DRM_AGENT_TENANT_ID"),
                currentUserRegistry("TenantId"),
                localMachineRegistry("TenantId")),
            FirstGuid(
                environment("DrmAgent__UserId"),
                environment("DRM_AGENT_USER_ID"),
                currentUserRegistry("UserId"),
                localMachineRegistry("UserId")),
            FirstGuid(
                environment("DrmAgent__DeviceId"),
                environment("DRM_AGENT_DEVICE_ID"),
                currentUserRegistry("DeviceId"),
                localMachineRegistry("DeviceId")),
            deviceSecret);
    }

    public AgentIdentity? TryCreateIdentity(AgentIdentityCacheEntry? cachedIdentity)
    {
        var tenantId = TenantId ?? cachedIdentity?.TenantId;
        var userId = UserId ?? cachedIdentity?.UserId;
        if (tenantId is null || userId is null || DeviceId is null)
        {
            return null;
        }

        return new AgentIdentity(tenantId.Value, userId.Value, DeviceId.Value, DeviceSecret);
    }

    private static string? ReadEnvironmentValue(string name)
        => Environment.GetEnvironmentVariable(name);

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryValue(RegistryHive hive, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(RegistryRootKey, writable: false);
            return key?.GetValue(valueName) as string;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static Guid? FirstGuid(params string?[] values)
    {
        foreach (var value in values)
        {
            if (Guid.TryParse(value?.Trim(), out var guid) && guid != Guid.Empty)
            {
                return guid;
            }
        }

        return null;
    }

    private static Uri? TryParseUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
