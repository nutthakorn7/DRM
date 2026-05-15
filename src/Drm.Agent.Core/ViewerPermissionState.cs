using Drm.Domain;

namespace Drm.Agent.Core;

public enum ViewerControlledAction
{
    Print,
    Copy,
    ExportOriginal
}

public sealed record ViewerPermissionState(
    bool CanPrint,
    bool CanCopy,
    bool CanExportOriginal)
{
    public static ViewerPermissionState From(Permission permissions)
        => new(
            permissions.HasFlag(Permission.Print),
            permissions.HasFlag(Permission.Copy),
            permissions.HasFlag(Permission.ExportOriginal));

    public bool Allows(ViewerControlledAction action)
        => action switch
        {
            ViewerControlledAction.Print => CanPrint,
            ViewerControlledAction.Copy => CanCopy,
            ViewerControlledAction.ExportOriginal => CanExportOriginal,
            _ => false
        };
}
