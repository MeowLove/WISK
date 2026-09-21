using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WindowsInitializer.Contracts;

namespace WindowsInitializer.App;

public sealed record InteractiveConfiguration(ImmutableDictionary<string, string> Parameters, ImmutableArray<ProfileAccount>? Accounts);

public partial class ConfigurationDialog : Window
{
    private readonly HashSet<string> _taskIds;
    private readonly ObservableCollection<AccountDraft> _accounts = [];
    public InteractiveConfiguration? Result { get; private set; }

    public ConfigurationDialog(IEnumerable<string> taskIds, InteractiveConfiguration? existing = null)
    {
        _taskIds = taskIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        AccountList.ItemsSource = _accounts;
        Localization.Apply(this, Localization.CurrentCode);
        ComputerNamePanel.Visibility = Has("computer-name") ? Visibility.Visible : Visibility.Collapsed;
        DeviceRegionPanel.Visibility = Has("device-setup-region") ? Visibility.Visible : Visibility.Collapsed;
        LanguageUiPanel.Visibility = Has("language-ui-preference") ? Visibility.Visible : Visibility.Collapsed;
        WindowsUpdateModePanel.Visibility = Has("setting-windows-update-mode") ? Visibility.Visible : Visibility.Collapsed;
        PowerPlanPanel.Visibility = Has("setting-power-plan") ? Visibility.Visible : Visibility.Collapsed;
        SleepTimeoutPanel.Visibility = Has("setting-sleep-timeouts") ? Visibility.Visible : Visibility.Collapsed;
        RegistryBooleanPanel.Visibility = HasRegistryBoolean() ? Visibility.Visible : Visibility.Collapsed;
        if (RegistryBooleanTaskId() is { } booleanTaskId)
        {
            RegistryBooleanTitle.Text = CatalogLocalization.TaskName(booleanTaskId,
                new WindowsInitializer.Core.Catalog().Find(booleanTaskId)?.DisplayName ?? booleanTaskId);
            RegistryBooleanHint.Text = Localization.Get(booleanTaskId == "setting-explorer-this-pc" ? "explorerLocationHint" : "registrySignOutHint");
            if (booleanTaskId == "setting-explorer-this-pc")
            {
                ((ComboBoxItem)RegistryBooleanBox.Items[0]).Content = Localization.Get("explorerThisPc");
                ((ComboBoxItem)RegistryBooleanBox.Items[1]).Content = Localization.Get("explorerHome");
            }
        }
        AccountPanel.Visibility = Has("accounts-local") ? Visibility.Visible : Visibility.Collapsed;
        NoInputText.Visibility = _taskIds.Any(RequiresInput) ? Visibility.Collapsed : Visibility.Visible;
        if (Has("computer-name")) ComputerNameBox.Text = Environment.MachineName;
        if (existing is not null)
        {
            foreach (var account in existing.Accounts ?? []) _accounts.Add(AccountDraft.From(account));
            if (existing.Parameters.TryGetValue("computer-name", out var computerName)) ComputerNameBox.Text = computerName;
            if (existing.Parameters.TryGetValue("device-setup-region", out var region)) DeviceRegionBox.Text = region;
            if (existing.Parameters.TryGetValue("language-ui-preference", out var language)) LanguageUiBox.Text = language;
            if (existing.Parameters.TryGetValue("setting-windows-update-mode", out var updateMode)) SelectTag(WindowsUpdateModeBox, updateMode);
            if (existing.Parameters.TryGetValue("setting-power-plan", out var powerPlan)) SelectTag(PowerPlanBox, powerPlan);
            if (existing.Parameters.TryGetValue("setting-sleep-timeouts", out var timeouts)) LoadTimeouts(timeouts);
            var registryTaskId = RegistryBooleanTaskId();
            if (registryTaskId is not null && existing.Parameters.TryGetValue(registryTaskId, out var registryState)) SelectTag(RegistryBooleanBox, registryState);
        }
    }

    public static bool RequiresInput(string taskId) => TaskConfigurationCatalog.RequiresInput(taskId);

    private bool Has(string id) => _taskIds.Contains(id);

    private bool HasRegistryBoolean() => RegistryBooleanTaskId() is not null;

    private string? RegistryBooleanTaskId() =>
        _taskIds.FirstOrDefault(TaskConfigurationCatalog.IsRegistryBoolean);

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var parameters = ImmutableDictionary<string, string>.Empty;
        if (Has("computer-name"))
        {
            var value = ComputerNameBox.Text.Trim();
            if (!Regex.IsMatch(value, "^[A-Za-z0-9-]{1,15}$")) { ShowInvalid("invalidComputerName"); return; }
            parameters = parameters.Add("computer-name", value);
        }
        if (Has("device-setup-region"))
        {
            var value = DeviceRegionBox.Text.Trim();
            if (!Regex.IsMatch(value, "^\\d{1,4}$")) { ShowInvalid("invalidRegion"); return; }
            parameters = parameters.Add("device-setup-region", value);
        }
        if (Has("language-ui-preference"))
        {
            var value = LanguageUiBox.Text.Trim();
            if (!Regex.IsMatch(value, "^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$")) { ShowInvalid("invalidLanguageTag"); return; }
            parameters = parameters.Add("language-ui-preference", value);
        }
        if (Has("setting-windows-update-mode"))
            parameters = parameters.Add("setting-windows-update-mode", SelectedTag(WindowsUpdateModeBox));
        if (Has("setting-power-plan"))
            parameters = parameters.Add("setting-power-plan", SelectedTag(PowerPlanBox));
        if (Has("setting-sleep-timeouts"))
        {
            if (!TryMinutes(SleepAcBox.Text, out var sleepAc) || !TryMinutes(SleepDcBox.Text, out var sleepDc) ||
                !TryMinutes(DisplayAcBox.Text, out var displayAc) || !TryMinutes(DisplayDcBox.Text, out var displayDc))
            {
                ShowInvalid("invalidTimeoutMinutes");
                return;
            }
            parameters = parameters.Add("setting-sleep-timeouts", $"ac={sleepAc};dc={sleepDc};displayAc={displayAc};displayDc={displayDc}");
        }
        var registryTaskId = RegistryBooleanTaskId();
        if (registryTaskId is not null)
            parameters = parameters.Add(registryTaskId, SelectedTag(RegistryBooleanBox));

        ImmutableArray<ProfileAccount>? accounts = null;
        if (Has("accounts-local"))
        {
            if (_accounts.Count == 0 && !string.IsNullOrWhiteSpace(AccountNameBox.Text))
                if (!TryAddCurrentAccount()) return;
            if (_accounts.Count == 0) { ShowInvalid("accountRequired"); return; }
            accounts = _accounts.Select(item => item.ToProfile()).ToImmutableArray();
        }

        Result = new InteractiveConfiguration(parameters, accounts);
        DialogResult = true;
    }

    private void ShowInvalid(string messageKey) => MessageBox.Show(
        Localization.Get(messageKey), Localization.Get("invalidValue"), MessageBoxButton.OK, MessageBoxImage.Warning);

    private static string SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? throw new InvalidOperationException("A configuration option must be selected.");

    private static void SelectTag(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase)) ?? comboBox.Items[0];
    }

    private void LoadTimeouts(string value)
    {
        var values = value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);
        if (values.TryGetValue("ac", out var sleepAc)) SleepAcBox.Text = sleepAc;
        if (values.TryGetValue("dc", out var sleepDc)) SleepDcBox.Text = sleepDc;
        if (values.TryGetValue("displayAc", out var displayAc)) DisplayAcBox.Text = displayAc;
        if (values.TryGetValue("displayDc", out var displayDc)) DisplayDcBox.Text = displayDc;
    }

    private static bool TryMinutes(string value, out int minutes) => int.TryParse(value.Trim(), out minutes) && minutes is >= 0 and <= 1440;

    private void AddAccount_Click(object sender, RoutedEventArgs e) => TryAddCurrentAccount();

    private void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is AccountDraft account) _accounts.Remove(account);
    }

    private bool TryAddCurrentAccount()
    {
        var name = AccountNameBox.Text.Trim();
        if (!Regex.IsMatch(name, "^[A-Za-z0-9._-]{1,20}$")) { ShowInvalid("invalidAccountName"); return false; }
        if (_accounts.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { ShowInvalid("duplicateAccount"); return false; }
        _accounts.Add(new AccountDraft(name, AccountFullNameBox.Text.Trim(),
            AccountAdminBox.IsChecked == true ? "S-1-5-32-544" : "S-1-5-32-545",
            AccountNeverExpiresBox.IsChecked == true, AccountHiddenBox.IsChecked == true, AccountRemoteDesktopBox.IsChecked == true,
            string.IsNullOrEmpty(AccountPasswordBox.Password) ? null : AccountPasswordBox.Password));
        AccountNameBox.Clear(); AccountFullNameBox.Clear(); AccountPasswordBox.Clear();
        AccountAdminBox.IsChecked = false; AccountNeverExpiresBox.IsChecked = false; AccountHiddenBox.IsChecked = false; AccountRemoteDesktopBox.IsChecked = false;
        return true;
    }

    private sealed record AccountDraft(string Name, string FullName, string GroupSid, bool PasswordNeverExpires, bool HideFromSignInScreen, bool AllowRemoteDesktop, string? Password)
    {
        public string DisplaySummary => $"{Name} · {Localization.Get(GroupSid == "S-1-5-32-544" ? "administrator" : "standardUser")}";
        public ProfileAccount ToProfile() => new(Name, FullName, null, GroupSid, PasswordNeverExpires, HideFromSignInScreen, AllowRemoteDesktop, Password);
        public static AccountDraft From(ProfileAccount account) => new(account.Name, account.FullName ?? string.Empty, account.GroupSid ?? "S-1-5-32-545", account.PasswordNeverExpires, account.HideFromSignInScreen, account.AllowRemoteDesktop, account.Password);
    }
}
