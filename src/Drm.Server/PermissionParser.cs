using Drm.Domain;

namespace Drm.Server;

internal static class PermissionParser
{
    private const Permission DefinedPermissions =
        Permission.View
        | Permission.Print
        | Permission.Copy
        | Permission.ExportOriginal
        | Permission.Edit
        | Permission.DeleteProtectedCopy;

    public static bool TryParse(string value, out Permission permissions)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse(value, ignoreCase: true, out permissions))
        {
            permissions = Permission.None;
            return false;
        }

        return (permissions & ~DefinedPermissions) == Permission.None;
    }
}
