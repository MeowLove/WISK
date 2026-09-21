using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;

namespace WindowsInitializer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyHighContrastResources();
        var envelopePath = FindArgument(e.Args, "--uac-envelope");
        var window = new MainWindow(envelopePath);
        MainWindow = window;
        window.Show();
        var snapshotPath = FindArgument(e.Args, "--capture-ui");
        if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            var language = FindArgument(e.Args, "--language") ?? "en";
            var dark = e.Args.Any(value => value.Equals("--dark", StringComparison.OrdinalIgnoreCase));
            window.PrepareForSnapshot(language, dark, FindArgument(e.Args, "--workspace"));
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
            {
                await Task.Delay(600);
                window.UpdateLayout();
                UiSnapshotCapture.Save(window, Path.GetFullPath(snapshotPath));
                window.Close();
                Shutdown(0);
            });
        }
    }

    private void ApplyHighContrastResources()
    {
        if (!SystemParameters.HighContrast) return;
        Resources["AppWindowBrush"] = SystemColors.WindowBrush;
        Resources["AppPanelBrush"] = SystemColors.WindowBrush;
        Resources["AppTextBrush"] = SystemColors.WindowTextBrush;
        Resources["AppMutedTextBrush"] = SystemColors.GrayTextBrush;
        Resources["AccentBrush"] = SystemColors.HighlightBrush;
        Resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;
        Resources["AppBorderBrush"] = SystemColors.ActiveBorderBrush;
        Resources["ElevatedRiskBrush"] = SystemColors.WindowBrush;
        Resources["HighRiskBrush"] = SystemColors.WindowBrush;
    }

    private static string? FindArgument(IReadOnlyList<string> args, string name)
    {
        var index = Array.FindIndex(args.ToArray(), value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }
}
