using Drm.Agent.Core;
using Microsoft.Extensions.Options;

namespace Drm.Agent.Service.Windows;

public class Worker(
    ILogger<Worker> logger,
    AgentHeartbeatWorkflow heartbeatWorkflow,
    IOptionsMonitor<AgentServiceOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = options.CurrentValue;
            if (!currentOptions.IsConfigured)
            {
                logger.LogWarning("DRM Agent is waiting for DrmAgent TenantId, UserId, and DeviceId configuration.");
                await Task.Delay(currentOptions.HeartbeatInterval, stoppingToken);
                continue;
            }

            try
            {
                await heartbeatWorkflow.ReportOnlineAsync(
                    currentOptions.ToIdentity(),
                    Environment.MachineName,
                    Environment.OSVersion.VersionString,
                    currentOptions.AgentVersion,
                    stoppingToken);

                logger.LogInformation("DRM Agent heartbeat sent at {Time}", DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "DRM Agent heartbeat failed.");
            }

            await Task.Delay(currentOptions.HeartbeatInterval, stoppingToken);
        }
    }
}
