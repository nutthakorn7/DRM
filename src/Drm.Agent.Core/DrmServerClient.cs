using System.Net.Http.Json;
using System.Text.Json;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed record ProtectedFileRegistration(
    Guid TenantId,
    Guid FileId,
    Guid OwnerUserId,
    string ContentType,
    DateTimeOffset ExpiresAtUtc,
    Permission Permissions,
    Guid? PolicyTemplateId,
    IReadOnlyList<ProtectionRecipient> Recipients);

public sealed record ProtectionRecipient(string SubjectType, Guid SubjectId);

public interface IDrmServerClient : IAgentAuditUploader
{
    Task RegisterFileAsync(
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId,
        string contentType,
        DateTimeOffset expiresAtUtc,
        Permission permissions,
        CancellationToken cancellationToken);

    Task RegisterFileAsync(ProtectedFileRegistration registration, CancellationToken cancellationToken)
        => RegisterFileAsync(
            registration.TenantId,
            registration.FileId,
            registration.OwnerUserId,
            registration.ContentType,
            registration.ExpiresAtUtc,
            registration.Permissions,
            cancellationToken);

    Task<OpenDecision> DecideAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        Permission permission,
        CancellationToken cancellationToken);

    Task<AgentDeviceRegistration> RegisterDeviceAsync(
        AgentIdentity identity,
        string hostname,
        string operatingSystem,
        string agentVersion,
        CancellationToken cancellationToken);

    Task<AgentHeartbeat> RecordHeartbeatAsync(
        AgentIdentity identity,
        string status,
        string agentVersion,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentCommand>> GetPendingCommandsAsync(
        AgentIdentity identity,
        CancellationToken cancellationToken);

    Task<AgentCommand> CompleteCommandAsync(
        AgentIdentity identity,
        Guid commandId,
        AgentCommandCompletion completion,
        CancellationToken cancellationToken);

    Task WrapFileKeyAsync(
        Guid tenantId,
        Guid fileId,
        byte[] fileKey,
        CancellationToken cancellationToken);

    Task<UnwrappedFileKey> UnwrapFileKeyAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermission,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolve a work-email to a (tenantId, userId, displayName,
    /// defaultPolicyTemplateId) tuple via GET /api/agent/discover.
    /// Returns null when the server replies 404 (email not registered)
    /// so callers can show "we couldn't find that email" without
    /// having to catch a status-code exception.
    /// Other failures (network, 4xx other than 404, 5xx) throw —
    /// those are real bugs the user should hear about.
    ///
    /// Default implementation throws NotImplementedException — kept
    /// as a default-interface-method so existing IDrmServerClient
    /// test stubs (RecordingServerClient, FakeDrmServerClient, etc.)
    /// don't need to be updated unless their tests exercise the
    /// first-run discovery path. Production DrmServerClient overrides.
    /// </summary>
    Task<AgentDiscoveryResult?> DiscoverAsync(string email, CancellationToken cancellationToken)
        => throw new NotImplementedException(
            "This IDrmServerClient implementation does not support agent discovery. " +
            "Use DrmServerClient or override DiscoverAsync in your test double.");
}

public sealed record OpenDecision(
    bool Allowed,
    string ReasonCode,
    string? WatermarkTemplate,
    Permission AllowedPermissions,
    DateTimeOffset? OfflineLeaseExpiresAtUtc);

public sealed record UnwrappedFileKey(
    byte[] FileKey,
    Permission AllowedPermissions,
    string? WatermarkTemplate,
    DateTimeOffset? OfflineLeaseExpiresAtUtc);

public sealed class DrmServerClient : IDrmServerClient
{
    public const string ClientApiKeyHeaderName = "X-DRM-Client-Key";

    private const Permission DefinedPermissions =
        Permission.View | Permission.Print | Permission.Copy |
        Permission.ExportOriginal | Permission.Edit | Permission.DeleteProtectedCopy;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    public DrmServerClient(HttpClient httpClient, string? clientApiKey = null)
    {
        this.httpClient = httpClient;
        if (!string.IsNullOrWhiteSpace(clientApiKey))
        {
            httpClient.DefaultRequestHeaders.Remove(ClientApiKeyHeaderName);
            httpClient.DefaultRequestHeaders.Add(ClientApiKeyHeaderName, clientApiKey.Trim());
        }
    }

    public async Task RegisterFileAsync(
        Guid tenantId,
        Guid fileId,
        Guid ownerUserId,
        string contentType,
        DateTimeOffset expiresAtUtc,
        Permission permissions,
        CancellationToken cancellationToken)
    {
        await RegisterFileAsync(
            new ProtectedFileRegistration(
                tenantId,
                fileId,
                ownerUserId,
                contentType,
                expiresAtUtc,
                permissions,
                PolicyTemplateId: null,
                Recipients: []),
            cancellationToken);
    }

    public async Task RegisterFileAsync(
        ProtectedFileRegistration registration,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/files",
            new RegisterFileRequest(
                registration.TenantId,
                registration.FileId,
                registration.OwnerUserId,
                registration.ContentType,
                registration.ExpiresAtUtc,
                registration.Permissions.ToString(),
                null,
                registration.PolicyTemplateId,
                registration.Recipients),
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
            ParsePermissionsOrNone(decision.AllowedPermissions),
            decision.OfflineLeaseExpiresAtUtc);
    }

    public async Task<AgentDeviceRegistration> RegisterDeviceAsync(
        AgentIdentity identity,
        string hostname,
        string operatingSystem,
        string agentVersion,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/agent/devices/register",
            new RegisterDeviceRequest(
                identity.TenantId,
                identity.UserId,
                identity.DeviceId,
                hostname,
                operatingSystem,
                agentVersion),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AgentDeviceRegistration>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Agent registration response was empty.");
    }

    public async Task<AgentHeartbeat> RecordHeartbeatAsync(
        AgentIdentity identity,
        string status,
        string agentVersion,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/agent/devices/{identity.DeviceId}/heartbeat",
            new HeartbeatRequest(
                identity.TenantId,
                identity.UserId,
                status,
                agentVersion),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AgentHeartbeat>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Agent heartbeat response was empty.");
    }

    public async Task UploadAuditAsync(AgentAuditRecord record, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/agent/audit",
            record,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<AgentCommand>> GetPendingCommandsAsync(
        AgentIdentity identity,
        CancellationToken cancellationToken)
    {
        var commands = await httpClient.GetFromJsonAsync<IReadOnlyList<AgentCommand>>(
            $"/api/agent/devices/{identity.DeviceId}/commands?tenantId={identity.TenantId}",
            JsonOptions,
            cancellationToken);

        return commands ?? [];
    }

    public async Task<AgentCommand> CompleteCommandAsync(
        AgentIdentity identity,
        Guid commandId,
        AgentCommandCompletion completion,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/agent/devices/{identity.DeviceId}/commands/{commandId}/complete",
            new CompleteCommandRequest(identity.TenantId, completion.Status, completion.ReasonCode),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AgentCommand>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Agent command completion response was empty.");
    }

    public async Task WrapFileKeyAsync(
        Guid tenantId,
        Guid fileId,
        byte[] fileKey,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/files/{fileId}/keys/wrap",
            new WrapFileKeyRequest(tenantId, Convert.ToBase64String(fileKey)),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<UnwrappedFileKey> UnwrapFileKeyAsync(
        Guid tenantId,
        Guid fileId,
        Guid userId,
        Guid deviceId,
        string requestedPermission,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/files/{fileId}/keys/unwrap",
            new UnwrapFileKeyRequest(tenantId, userId, deviceId, requestedPermission),
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var unwrapped = await response.Content.ReadFromJsonAsync<UnwrapFileKeyResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("File key unwrap response was empty.");

        return new UnwrappedFileKey(
            Convert.FromBase64String(unwrapped.FileKeyBase64),
            ParsePermissionsOrNone(unwrapped.AllowedPermissions),
            unwrapped.WatermarkTemplate,
            unwrapped.OfflineLeaseExpiresAtUtc);
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

    /// <summary>
    /// GET /api/agent/discover?email=...
    /// The server returns 200 + body for a known active user, 404 for
    /// an unknown one, 400 for malformed email.  This wrapper turns
    /// 404 into a returned null (since "not found" is a normal flow
    /// for the first-run dialog — the user just retypes the email).
    /// </summary>
    public async Task<AgentDiscoveryResult?> DiscoverAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email must be supplied.", nameof(email));
        }

        // Uri.EscapeDataString catches the '+' and '@' characters that
        // are legal in email local-parts but mean other things in URL
        // query syntax.  GetAsync with a relative URI uses the
        // HttpClient.BaseAddress.
        var path = $"/api/agent/discover?email={Uri.EscapeDataString(email.Trim())}";
        using var response = await httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AgentDiscoveryResult>(
            JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Server returned an empty discover response.");
    }

    private sealed record RegisterFileRequest(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string ContentType,
        DateTimeOffset ExpiresAtUtc,
        string Permissions,
        string? WatermarkTemplate,
        Guid? PolicyTemplateId,
        IReadOnlyList<ProtectionRecipient> Recipients);

    private sealed record DecidePolicyRequest(
        Guid TenantId,
        Guid FileId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission,
        DateTimeOffset? AtUtc);

    private sealed record RegisterDeviceRequest(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string Hostname,
        string OperatingSystem,
        string AgentVersion);

    private sealed record HeartbeatRequest(
        Guid TenantId,
        Guid UserId,
        string Status,
        string AgentVersion);

    private sealed record CompleteCommandRequest(Guid TenantId, string Status, string ReasonCode);

    private sealed record WrapFileKeyRequest(Guid TenantId, string FileKeyBase64);

    private sealed record UnwrapFileKeyRequest(
        Guid TenantId,
        Guid UserId,
        Guid DeviceId,
        string RequestedPermission);

    private sealed record UnwrapFileKeyResponse(
        Guid TenantId,
        Guid FileId,
        string FileKeyBase64,
        string? AllowedPermissions,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc);

    private sealed record PolicyDecisionResponse(
        bool Allowed,
        string? AllowedPermissions,
        string ReasonCode,
        string? WatermarkTemplate,
        DateTimeOffset? OfflineLeaseExpiresAtUtc);
}
