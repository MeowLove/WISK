using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using WindowsInitializer.Contracts;
using WindowsInitializer.Core;

namespace WindowsInitializer.Platform.Windows;

public interface IWindowsSystemProbe
{
    string? ReadCurrentVersion(string name);
    bool FileExists(string path);
    bool IsAdministrator();
    bool IsNetworkAvailable();
    bool IsProxyConfigured();
    bool IsWindowsUpdateAvailable();
    string OsArchitecture { get; }
    string ProcessArchitecture { get; }
}

public sealed class WindowsCompatibility
{
    public const int MinimumSupportedBuild = 26100;
    private readonly IWindowsSystemProbe _probe;

    public WindowsCompatibility(IWindowsSystemProbe? probe = null) => _probe = probe ?? new DefaultWindowsSystemProbe();

    public CompatibilitySnapshot Read()
    {
        var productName = _probe.ReadCurrentVersion("ProductName") ?? "Unknown Windows";
        var displayVersion = _probe.ReadCurrentVersion("DisplayVersion") ?? string.Empty;
        var buildText = _probe.ReadCurrentVersion("CurrentBuildNumber") ?? "0";
        var ubrText = _probe.ReadCurrentVersion("UBR") ?? "0";
        _ = int.TryParse(buildText, out var build);
        _ = int.TryParse(ubrText, out var ubr);
        var isWindowsClient = productName.Contains("Windows", StringComparison.OrdinalIgnoreCase) &&
                              !productName.Contains("Server", StringComparison.OrdinalIgnoreCase);
        var isWindows11 = isWindowsClient && build >= 22000;
        var isSupportedArchitecture = string.Equals(_probe.OsArchitecture, "x64", StringComparison.OrdinalIgnoreCase) &&
                                      string.Equals(_probe.ProcessArchitecture, "x64", StringComparison.OrdinalIgnoreCase);
        var isSupportedBuild = isWindows11 && build >= MinimumSupportedBuild;
        var powershell = Environment.GetEnvironmentVariable("SystemRoot") is { Length: > 0 } systemRoot &&
                         _probe.FileExists(Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"));
        var winget = Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } localAppData &&
                     _probe.FileExists(Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe"));
        return new CompatibilitySnapshot(
            productName,
            displayVersion,
            build,
            ubr,
            _probe.ReadCurrentVersion("EditionID") ?? string.Empty,
            _probe.OsArchitecture,
            _probe.ProcessArchitecture,
            isWindows11,
            isSupportedBuild,
            _probe.IsAdministrator(),
            powershell,
            winget,
            _probe.IsNetworkAvailable(),
            _probe.IsProxyConfigured(),
            _probe.IsWindowsUpdateAvailable(),
            isSupportedBuild && isSupportedArchitecture);
    }

    private sealed class DefaultWindowsSystemProbe : IWindowsSystemProbe
    {
        public string OsArchitecture => RuntimeInformation.OSArchitecture.ToString();
        public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

        public string? ReadCurrentVersion(string name)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return key?.GetValue(name)?.ToString();
            }
            catch (Exception) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }
        }

        public bool FileExists(string path) => File.Exists(path);

        public bool IsAdministrator()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public bool IsNetworkAvailable() => NetworkInterface.GetIsNetworkAvailable();

        public bool IsProxyConfigured() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTP_PROXY")) ||
                                           !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTPS_PROXY"));

        public bool IsWindowsUpdateAvailable() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            (ServiceControllerHelper.IsServiceRunning("wuauserv") || ServiceControllerHelper.IsServiceRunning("UsoSvc"));
    }

    private static class ServiceControllerHelper
    {
        public static bool IsServiceRunning(string name)
        {
            try
            {
                using var service = new System.ServiceProcess.ServiceController(name);
                return service.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            }
            catch (Exception) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }
        }
    }
}

public static class ExecutionEnvironmentProbe
{
    public static ExecutionEnvironmentSnapshot Read()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var root = Path.GetPathRoot(systemRoot);
        var freeBytes = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root)
            ? new DriveInfo(root).AvailableFreeSpace
            : 0;
        var protectionManager = !string.IsNullOrWhiteSpace(systemRoot) &&
                                File.Exists(Path.Combine(systemRoot, "System32", "SystemPropertiesProtection.exe"));
        return new ExecutionEnvironmentSnapshot(freeBytes, HasPendingReboot(), protectionManager);
    }

    private static bool HasPendingReboot()
    {
        try
        {
            using var servicing = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var update = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var session = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            return servicing is not null || update is not null || session?.GetValue("PendingFileRenameOperations") is not null;
        }
        catch (Exception) when (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }
    }
}
