namespace Drm.Domain;

[Flags]
public enum Permission
{
    None = 0,
    View = 1 << 0,
    Print = 1 << 1,
    Copy = 1 << 2,
    ExportOriginal = 1 << 3,
    Edit = 1 << 4,
    DeleteProtectedCopy = 1 << 5
}
