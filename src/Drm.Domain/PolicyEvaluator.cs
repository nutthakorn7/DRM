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

        // Access count enforcement. MaxOpens null means unlimited. If the user
        // has already consumed the allowed number of opens, deny. The caller
        // is responsible for incrementing OpensUsed on a successful access.
        int? opensRemaining = null;
        if (policy.MaxOpens.HasValue)
        {
            var remaining = policy.MaxOpens.Value - policy.OpensUsed;
            if (remaining <= 0)
            {
                return PolicyDecision.Deny("opens_exhausted", opensRemaining: 0);
            }

            // Report what will be left AFTER this access is consumed by the
            // caller. This is the value most clients display ("3 opens left").
            opensRemaining = remaining - 1;
        }

        return PolicyDecision.Allow(grant.Permissions, policy.WatermarkTemplate, opensRemaining);
    }
}
