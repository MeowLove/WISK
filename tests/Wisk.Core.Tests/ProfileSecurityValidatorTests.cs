using System.Security.AccessControl;
using System.Security.Principal;
using Wisk.Platform.Windows;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ProfileSecurityValidatorTests
{
    [Fact]
    public void PrivateProfileAclIsAccepted()
    {
        var path = CreateProfileFile(allowEveryoneRead: false);
        try { ProfileSecurityValidator.ValidatePrivateFile(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BroadProfileReadAclIsRejected()
    {
        var path = CreateProfileFile(allowEveryoneRead: true);
        try { Assert.Throws<UnauthorizedAccessException>(() => ProfileSecurityValidator.ValidatePrivateFile(path)); }
        finally { File.Delete(path); }
    }

    private static string CreateProfileFile(bool allowEveryoneRead)
    {
        var path = Path.Combine(Path.GetTempPath(), "WISK wisk-profile-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{}");
        var currentSid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows identity is unavailable.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(currentSid, FileSystemRights.FullControl, AccessControlType.Allow));
        if (allowEveryoneRead)
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
        return path;
    }
}
