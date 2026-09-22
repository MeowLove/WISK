namespace Wisk.Contracts;

public static class RegistrySourceFiles
{
    public const string User = "WISK_User.reg";
    public const string System = "WISK_System.reg";

    public static IReadOnlyList<string> All { get; } = [User, System];
}
