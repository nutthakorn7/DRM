using System.IO.Pipes;
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

            await using var pipe = new NamedPipeServerStream(
                DeviceSigningPipe.PipeName(current.DeviceId),
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
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
