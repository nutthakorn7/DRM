using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Drm.Agent.Core;
using Drm.Domain;
using Microsoft.Extensions.Options;

namespace Drm.Agent.Service.Windows;

public sealed class DeviceSigningPipeServer(
    ILogger<DeviceSigningPipeServer> logger,
    IOptionsMonitor<AgentServiceOptions> options) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = options.CurrentValue;
            if (!current.IsConfigured || string.IsNullOrWhiteSpace(current.DeviceSecret))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            // PR #50 hardening: the signing service runs as LocalSystem and
            // holds the device secret in memory. Without an ACL the pipe used
            // the default DACL and performed no caller authentication, so any
            // local process that knew the (non-secret) device id could request
            // a signature — a local signing oracle. Restrict the pipe to
            // SYSTEM + Administrators (full) and the interactive logged-on user
            // (read/write, so the viewer can connect). This denies NETWORK
            // (remote), ANONYMOUS, and non-interactive sandboxed service
            // accounts. NOTE: "Interactive" still admits any interactive user
            // on a multi-session host (RDS/terminal server); the follow-up is
            // to grant only the specific provisioned user's SID — see PR notes.
            await using var pipe = NamedPipeServerStreamAcl.Create(
                DeviceSigningPipe.PipeName(current.DeviceId),
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                CreatePipeSecurity());

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                LogCaller(pipe);
                await HandleRequestAsync(pipe, current, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Device signing pipe request failed.");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        // Interactive logged-on users (the viewer runs in the user's session).
        // ReadWrite is all a client needs — SYNCHRONIZE is auto-added to every
        // Allow ACE by PipeAccessRule, so connect succeeds. We deliberately do
        // NOT grant CreateNewInstance: a client never creates instances, and
        // granting it would let a non-admin user squat the pipe name and DoS
        // the viewer. Only the service (SYSTEM) creates instances.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }

    private void LogCaller(NamedPipeServerStream pipe)
    {
        // Best-effort audit of who connected. Impersonate the client just long
        // enough to read its identity; never sign under impersonation.
        try
        {
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                logger.LogInformation("Device signing pipe request from {Caller}.", identity.Name);
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not resolve device signing pipe caller identity.");
        }
    }

    private static async Task HandleRequestAsync(
        Stream pipe,
        AgentServiceOptions options,
        CancellationToken cancellationToken)
    {
        var request = await JsonSerializer.DeserializeAsync<DeviceSigningPipe.Request>(
            pipe,
            JsonOptions,
            cancellationToken);
        var response = Sign(request, options);
        await JsonSerializer.SerializeAsync(pipe, response, JsonOptions, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    private static DeviceSigningPipe.Response Sign(
        DeviceSigningPipe.Request? request,
        AgentServiceOptions options)
    {
        if (request is null)
        {
            return DeviceSigningPipe.Response.Error("invalid_request");
        }

        if (!string.Equals(request.Operation, "unwrap", StringComparison.OrdinalIgnoreCase) ||
            request.TenantId != options.TenantId ||
            request.UserId != options.UserId ||
            request.DeviceId != options.DeviceId ||
            string.IsNullOrWhiteSpace(options.DeviceSecret))
        {
            return DeviceSigningPipe.Response.Error("signing_denied");
        }

        var payload = DeviceRequestSigning.UnwrapPayload(
            request.TenantId,
            request.FileId,
            request.UserId,
            request.DeviceId,
            request.RequestedPermission);

        return DeviceSigningPipe.Response.Signed(
            DeviceRequestSigning.Sign(options.DeviceSecret, payload));
    }
}
