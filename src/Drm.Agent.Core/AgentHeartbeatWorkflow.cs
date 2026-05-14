namespace Drm.Agent.Core;

public sealed class AgentHeartbeatWorkflow(IDrmServerClient serverClient, IAgentAuditQueue auditQueue)
{
    public async Task ReportOnlineAsync(
        AgentIdentity identity,
        string hostname,
        string operatingSystem,
        string agentVersion,
        CancellationToken cancellationToken)
    {
        await serverClient.RegisterDeviceAsync(
            identity,
            hostname,
            operatingSystem,
            agentVersion,
            cancellationToken);

        await serverClient.RecordHeartbeatAsync(
            identity,
            "online",
            agentVersion,
            cancellationToken);

        await auditQueue.FlushAsync(cancellationToken);
    }
}
