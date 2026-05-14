using Drm.Agent.Core;

namespace Drm.Agent.Service.Windows;

public sealed class AgentServiceOptions
{
    public string ServerUrl { get; set; } = "http://localhost:5188";

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public Guid DeviceId { get; set; }

    public string AuditQueuePath { get; set; } = "%ProgramData%\\DRM\\agent-audit.jsonl";

    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public string AgentVersion { get; set; } = typeof(AgentServiceOptions).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServerUrl) &&
        TenantId != Guid.Empty &&
        UserId != Guid.Empty &&
        DeviceId != Guid.Empty;

    public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(Math.Max(5, HeartbeatIntervalSeconds));

    public AgentIdentity ToIdentity() => new(TenantId, UserId, DeviceId);

    public string ResolveAuditQueuePath()
    {
        var expanded = Environment.ExpandEnvironmentVariables(AuditQueuePath);
        var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonApplicationData))
        {
            commonApplicationData = Path.Combine(AppContext.BaseDirectory, "data");
        }

        return expanded.Replace("%ProgramData%", commonApplicationData, StringComparison.OrdinalIgnoreCase);
    }
}
