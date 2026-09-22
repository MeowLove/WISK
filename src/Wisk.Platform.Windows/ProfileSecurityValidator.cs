using System.Security.AccessControl;
using System.Security.Principal;

namespace Wisk.Platform.Windows;

public static class ProfileSecurityValidator
{
    public static void ValidatePrivateFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Profile file was not found.", fullPath);

        var security = new FileInfo(fullPath).GetAccessControl(AccessControlSections.Access);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is not SecurityIdentifier sid) continue;
            if (!IsBroadIdentity(sid)) continue;
            if ((rule.FileSystemRights & (FileSystemRights.ReadData | FileSystemRights.Read | FileSystemRights.FullControl)) != 0)
                throw new UnauthorizedAccessException("Profile files containing secrets must not grant broad read access.");
        }
    }

    private static bool IsBroadIdentity(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.WorldSid) ||
        sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid) ||
        sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid) ||
        sid.IsWellKnown(WellKnownSidType.BuiltinGuestsSid);
}
