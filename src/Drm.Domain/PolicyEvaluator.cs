namespace Drm.Domain;

public static class PolicyEvaluator
{
    public static PolicyDecision Evaluate(FilePolicy policy, PolicyRequest request)
    {
        if (policy.TenantId != request.TenantId)
        {
            return PolicyDecision.Deny("tenant_mismatch");
        }

        if (policy.FileId != request.FileId)
        {
            return PolicyDecision.Deny("file_mismatch");
        }

        if (policy.Revoked)
        {
            return PolicyDecision.Deny("revoked");
        }

        if (request.AtUtc > policy.ExpiresAtUtc)
        {
            return PolicyDecision.Deny("expired");
        }

        var grant = policy.Grants.FirstOrDefault(candidate => candidate.UserId == request.UserId);
        if (grant is null)
        {
            return PolicyDecision.Deny("no_grant");
        }

        if ((grant.Permissions & request.RequestedPermission) != request.RequestedPermission)
        {
            return PolicyDecision.Deny("permission_not_granted");
        }

        return PolicyDecision.Allow(grant.Permissions, policy.WatermarkTemplate);
    }
}
