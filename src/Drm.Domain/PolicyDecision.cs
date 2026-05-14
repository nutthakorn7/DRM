namespace Drm.Domain;

public sealed record PolicyDecision(
    bool Allowed,
    Permission AllowedPermissions,
    string ReasonCode,
    string? WatermarkTemplate)
{
    public static PolicyDecision Allow(Permission allowedPermissions, string watermarkTemplate)
        => new(true, allowedPermissions, "allowed", watermarkTemplate);

    public static PolicyDecision Deny(string reasonCode)
        => new(false, Permission.None, reasonCode, WatermarkTemplate: null);
}
