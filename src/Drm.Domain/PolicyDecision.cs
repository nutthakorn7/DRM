namespace Drm.Domain;

public sealed record PolicyDecision(
    bool Allowed,
    Permission AllowedPermissions,
    string ReasonCode,
    string? WatermarkTemplate,
    int? OpensRemaining = null)
{
    public static PolicyDecision Allow(Permission allowedPermissions, string watermarkTemplate, int? opensRemaining = null)
        => new(true, allowedPermissions, "allowed", watermarkTemplate, opensRemaining);

    public static PolicyDecision Deny(string reasonCode, int? opensRemaining = null)
        => new(false, Permission.None, reasonCode, WatermarkTemplate: null, OpensRemaining: opensRemaining);
}
