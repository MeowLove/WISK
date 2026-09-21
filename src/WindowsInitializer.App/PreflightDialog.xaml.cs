using WindowsInitializer.Core;

namespace WindowsInitializer.App;

public partial class PreflightDialog : Wpf.Ui.Controls.FluentWindow
{
    public PreflightDialog(ExecutionPreflightReport report, bool allowContinue)
    {
        InitializeComponent();
        ChecksGrid.ItemsSource = report.Checks.Select(check => new PreflightCheckRow(check)).ToArray();
        SummaryText.Text = report.CanExecute
            ? Localization.Format("preflightPassedSummary", report.WarningCount)
            : Localization.Format("preflightBlockedSummary", report.BlockingCount, report.WarningCount);
        ContinueButton.IsEnabled = allowContinue && report.CanExecute;
        ContinueButton.Visibility = allowContinue ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void Continue_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class PreflightCheckRow(ExecutionPreflightCheck check)
    {
        public string Glyph => check.Status switch { PreflightStatus.Passed => "✓", PreflightStatus.Warning => "!", _ => "×" };
        public string Name => Localization.Get("preflightCheck" + check.Id);
        public string Status => Localization.Get("preflightStatus" + check.Status);
        public string Message => Localization.Get(check.MessageKey);
    }
}
