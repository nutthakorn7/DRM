namespace Drm.Agent.Core;

public static class ViewerActionAudit
{
    public static AgentAuditRecord Create(
        AgentIdentity identity,
        Guid fileId,
        ViewerControlledAction action,
        bool allowed,
        DateTimeOffset atUtc)
    {
        var (eventType, reasonCode) = (action, allowed) switch
        {
            (ViewerControlledAction.Print, true) => ("print_allowed", "allowed"),
            (ViewerControlledAction.Print, false) => ("print_blocked", "missing_print_permission"),
            (ViewerControlledAction.Copy, true) => ("copy_allowed", "allowed"),
            (ViewerControlledAction.Copy, false) => ("copy_blocked", "missing_copy_permission"),
            (ViewerControlledAction.ExportOriginal, true) => ("export_allowed", "allowed"),
            (ViewerControlledAction.ExportOriginal, false) => ("export_blocked", "missing_export_permission"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown viewer action.")
        };

        return new AgentAuditRecord(
            identity.TenantId,
            identity.UserId,
            identity.DeviceId,
            fileId,
            eventType,
            reasonCode,
            atUtc);
    }
}
