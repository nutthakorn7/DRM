using System.Net.Http.Json;
using System.Text.Json;
using Drm.Domain;

namespace Drm.Agent.Core;

public interface IDrmServerClient
{
    Task RegisterFileAsync(
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId,
        string contentType,
        DateTimeOffset expiresAtUtc,
        Permission permissions,
        CancellationToken cancellationToken);

    Task<OpenDecision> DecideAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        Permission permission,
        CancellationToken cancellationToken);
}

public sealed record OpenDecision(
    bool Allowed,
    string ReasonCode,
    string? WatermarkTemplate,
    Permission AllowedPermissions);

public sealed class DrmServerClient(HttpClient httpClient) : IDrmServerClient
{
    private const Permission DefinedPermissions =
        Permission.View | Permission.Print | Permission.Copy |
        Permission.ExportOriginal | Permission.Edit | Permission.DeleteProtectedCopy;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RegisterFileAsync(
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId,
        string contentType,
        DateTimeOffset expiresAtUtc,
        Permission permissions,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/files",
            new RegisterFileRequest(
                tenantId,
                fileId,
                ownerUserId,
                contentType,
                expiresAtUtc,
                permissions.ToString(),
                null),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<OpenDecision> DecideAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/policy/decide",
            new DecidePolicyRequest(
                tenantId,
                fileId,
                userId,
                deviceId,
                permission.ToString(),
                null),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var decision = await response.Content.ReadFromJsonAsync<PolicyDecisionResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Policy decision response was empty.");

        return new OpenDecision(
            decision.Allowed,
            decision.ReasonCode,
            decision.WatermarkTemplate,
            ParsePermissionsOrNone(decision.AllowedPermissions));
    }

    private static Permission ParsePermissionsOrNone(string? permissions)
    {
        if (string.IsNullOrWhiteSpace(permissions))
        {
            return Permission.None;
        }

        if (Enum.TryParse<Permission>(permissions, ignoreCase: true, out var parsed) &&
            (parsed & ~DefinedPermissions) == Permission.None)
        {
            return parsed;
        }

        throw new InvalidOperationException($"Policy decision returned invalid permissions '{permissions}'.");
    }

    private sealed record RegisterFileRequest(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string? WatermarkTemplate);

    private sealed record DecidePolicyRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission,
        DateTimeOffset? AtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        string? AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate);
}
