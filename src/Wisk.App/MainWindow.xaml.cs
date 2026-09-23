using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Globalization;
using Microsoft.Win32;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.Execution;
using Wisk.Platform.Windows;
using Wisk.PowerShell;
using Wpf.Ui.Appearance;

namespace Wisk.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly JsonSerializerOptions PlanJsonOptions = new(JsonSerializerDefaults.Web);
    private Catalog _catalog = new();
    private readonly WindowsCompatibility _compatibility = new();
    private readonly ObservableCollection<TaskRow> _allRows = [];
    private readonly ObservableCollection<TaskRow> _visibleRows = [];
    private readonly ObservableCollection<ExecutionRow> _executionRows = [];
    private readonly ObservableCollection<CompletedRunRow> _completedRuns = [];
    private readonly ObservableCollection<CompletedTaskRow> _completedTaskRows = [];
    private readonly ObservableCollection<RecoveryRunRow> _recoveryRuns = [];
    private readonly ObservableCollection<RecoveryRecordRow> _recoveryRecords = [];
    private readonly List<DraftPlanItem> _draftItems = [];
    private ExecutionPolicy _draftPolicy = new();
    private CancellationTokenSource? _runCancellation;
    private bool _darkTheme;
    private readonly string? _uacEnvelopePath;
    private WorkspaceArea _activeArea = WorkspaceArea.Home;
    private bool _executionPaneExpanded = true;
    private readonly AppSettingsStore _settingsStore = new();
    private AppSettings _settings = new();
    private bool _loadingLanguage;
    private ComboBox? _languageSelector;
    private string _systemCategory = string.Empty;
    private string _registryCategory = string.Empty;
    private string _softwareCategory = string.Empty;
    private string _softwareStatusFilter = "available";
    private bool _systemCategoriesExpanded;
    private bool _registryCategoriesExpanded;
    private bool _softwareCategoriesExpanded;
    private bool _showCompletedView;
    private bool _planLocked;
    private const double CollapsedCategoryHeight = 84;

    public MainWindow(string? uacEnvelopePath = null)
    {
        _uacEnvelopePath = uacEnvelopePath;
        InitializeComponent();
        Title = $"WISK {BuildInfo.DisplayVersion}";
        AppTitleBar.Title = Title;
        ApplicationThemeManager.Apply(this);
        _settings = _settingsStore.Load();
        AddLanguageSelector();
        SystemGrid.ItemsSource = _visibleRows;
        SoftwareGrid.ItemsSource = _visibleRows;
        RegistryGrid.ItemsSource = _visibleRows;
        ExecutionGrid.ItemsSource = _executionRows;
        CompletedRunsGrid.ItemsSource = _completedRuns;
        CompletedTasksGrid.ItemsSource = _completedTaskRows;
        RecoveryRunsGrid.ItemsSource = _recoveryRuns;
        RecoveryRecordsGrid.ItemsSource = _recoveryRecords;
        ThemeButton.IsEnabled = !SystemParameters.HighContrast;
        if (SystemParameters.HighContrast) ThemeButton.ToolTip = Localization.Get("highContrastActive");
        LoadCatalogRows();
        ApplyFilter();
        RefreshStatus();
        UpdateActiveNavigation();
        Loaded += MainWindow_Loaded;
        SizeChanged += (_, _) => { UpdateResponsiveLayout(); ScheduleCategoryLayoutUpdate(); };
        UpdateResponsiveLayout();
    }

    private void AddLanguageSelector()
    {
        _languageSelector = new ComboBox { ItemsSource = Localization.Languages, DisplayMemberPath = nameof(UiLanguage.NativeName), Style = (Style)FindResource("PreferenceComboBoxStyle"), HorizontalAlignment = HorizontalAlignment.Stretch };
        _languageSelector.SelectionChanged += Language_Changed;
        LanguageHost.Content = _languageSelector;
        var code = Localization.Languages.FirstOrDefault(language => language.Code.Equals(_settings.Language, StringComparison.OrdinalIgnoreCase))?.Code
            ?? Localization.Languages.FirstOrDefault(language => language.Code.Equals(CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase))?.Code
            ?? "en";
        _loadingLanguage = true;
        _languageSelector.SelectedItem = Localization.Languages.First(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        Localization.Apply(this, code);
        _loadingLanguage = false;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_languageSelector?.SelectedItem is not UiLanguage language) return;
        Localization.Apply(this, language.Code);
        if (_loadingLanguage) return;

        _settings = _settings with { Language = language.Code };
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExecutionText.Text = string.Format(Localization.Get("settingsSaveFailed"), SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    public void LanguageCodeChanged(string code)
    {
        foreach (var row in _allRows) row.RefreshLocalization();
        foreach (var row in _executionRows) row.RefreshLocalization();
        RefreshCategoryTabs();
        ApplyFilter();
        RefreshStatus();
        RefreshExecutionPaneText();
        foreach (var row in _completedRuns) row.RefreshLocalization();
        foreach (var row in _completedTaskRows) row.RefreshLocalization();
        CompletedTaskDetailsText.Text = CompletedTasksGrid.SelectedItem is CompletedTaskRow selected
            ? selected.Details
            : Localization.Get("selectCompletedTask");
        CompletedViewButton.Content = Localization.Format("completedRunsCount", _completedRuns.Count);
        if (_activeArea == WorkspaceArea.Recovery) _ = RefreshRecoveryAsync();
        PageTitleText.Text = _activeArea switch { WorkspaceArea.System => Localization.Get("system"), WorkspaceArea.Registry => Localization.Get("registryOptimization"), WorkspaceArea.Software => Localization.Get("software"), WorkspaceArea.Recovery => Localization.Get("recoveryCenter"), _ => Localization.Get("home") };
        PageSubtitleText.Text = _activeArea switch { WorkspaceArea.System => Localization.Get("settingsSubtitle"), WorkspaceArea.Registry => Localization.Get("registrySubtitle"), WorkspaceArea.Software => Localization.Get("softwareSubtitle"), WorkspaceArea.Recovery => Localization.Get("recoverySubtitle"), _ => Localization.Get("safePlan") };
        ThemeButton.Content = Localization.Get(_darkTheme ? "lightTheme" : "darkTheme");
        UpdateCategoryExpansionLabels();
        ScheduleCategoryLayoutUpdate();
    }

    public void PrepareForSnapshot(string languageCode, bool darkTheme, string? workspace = null)
    {
        Width = 1440;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 20;
        Top = 20;
        var language = Localization.Languages.FirstOrDefault(item => item.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase)) ?? Localization.Languages[0];
        _loadingLanguage = true;
        if (_languageSelector is not null) _languageSelector.SelectedItem = language;
        Localization.Apply(this, language.Code);
        _loadingLanguage = false;
        ApplyTheme(darkTheme);
        if (Enum.TryParse<WorkspaceArea>(workspace, true, out var area)) Navigate(area);
        UpdateLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (SidebarColumn is null || ContentColumnHost is null || ExecutionPaneColumn is null) return;
        var compact = ActualWidth > 0 && ActualWidth < 1320;
        SidebarColumn.Width = new GridLength(compact ? 220 : 264);
        ContentColumnHost.Margin = compact ? new Thickness(22, 24, 18, 24) : new Thickness(36, 30, 28, 30);
        if (_executionPaneExpanded) ExecutionPaneColumn.Width = new GridLength(compact ? 330 : 380);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try { await ReloadCatalogAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("extensionCatalogRefreshFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
        ScheduleCategoryLayoutUpdate();
        try { await RefreshCompletedRunsAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExecutionText.Text = Localization.Format("runHistoryUnavailable", SensitiveDataRedactor.Redact(exception.Message));
        }
        if (!string.IsNullOrWhiteSpace(_uacEnvelopePath)) UacEnvelope_Loaded(sender, e);
    }

    private void RefreshStatus()
    {
        var snapshot = _compatibility.Read();
        HomeCompatibilityText.Text = $"{snapshot.ProductName} {snapshot.DisplayVersion}\nBuild {snapshot.Build} | {Localization.Get(snapshot.IsApplySupported ? "readyToApply" : "planOnly")}";
        UpdateSelectionSummary();
    }

    private void ApplyFilter()
    {
        var isSoftware = _activeArea == WorkspaceArea.Software;
        var isRegistry = _activeArea == WorkspaceArea.Registry;
        var query = (isSoftware ? SoftwareSearchBox?.Text : isRegistry ? RegistrySearchBox?.Text : SearchBox?.Text)?.Trim() ?? string.Empty;
        var selectedCategory = isSoftware ? _softwareCategory : isRegistry ? _registryCategory : _systemCategory;
        _visibleRows.Clear();
        var rows = _allRows.Where(row =>
                      (_activeArea == WorkspaceArea.Home || (isRegistry ? row.Kind == TaskKind.RegistrySetting : isSoftware ? IsSoftwareTask(row) : !IsSoftwareTask(row))) &&
                      (!isSoftware || row.MatchesSoftwareFilter(_softwareStatusFilter)) &&
                      (string.IsNullOrEmpty(query) || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Location.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Domain.Contains(query, StringComparison.OrdinalIgnoreCase) || row.PackageId.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                      (string.IsNullOrEmpty(selectedCategory) || row.RawDomain.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase)));
        if (isRegistry) rows = rows.OrderBy(row => row.Domain, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        if (isSoftware) rows = rows.OrderByDescending(row => row.IsAvailable).ThenBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase);
        foreach (var row in rows)
            _visibleRows.Add(row);
        UpdateSelectionSummary();
    }

    private static bool IsSoftwareTask(TaskRow row) => row.Kind is TaskKind.Winget or TaskKind.OfflineAsset;

    private void SoftwareStatusFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string filter }) return;
        _softwareStatusFilter = filter;
        foreach (var button in SoftwareStatusFilters.Children.OfType<Button>())
            button.Tag = Equals(button.CommandParameter, filter);
        ApplyFilter();
    }

    private void UpdateSelectionSummary()
    {
        var selected = _draftItems.Count;
        var summary = string.Format(Localization.Get("selectedShown"), selected, _visibleRows.Count);
        if (SelectionText is not null) SelectionText.Text = summary;
        if (SoftwareSelectionText is not null) SoftwareSelectionText.Text = summary;
        if (RegistrySelectionText is not null) RegistrySelectionText.Text = summary;
        if (HomeSelectionText is not null) HomeSelectionText.Text = string.Format(Localization.Get("tasksSelected"), selected);
        if (HomeTaskCountText is not null) HomeTaskCountText.Text = selected.ToString();
        if (QueueCountText is not null) QueueCountText.Text = string.Format(Localization.Get("queueCount"), _executionRows.Count);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void CategoryTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string key }) return;
        switch (_activeArea)
        {
            case WorkspaceArea.System: _systemCategory = key; break;
            case WorkspaceArea.Registry: _registryCategory = key; break;
            case WorkspaceArea.Software: _softwareCategory = key; break;
        }
        RefreshCategoryTabs();
        ApplyFilter();
    }

    private void CategoryExpansion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string area }) return;
        switch (area)
        {
            case "System": _systemCategoriesExpanded = !_systemCategoriesExpanded; break;
            case "Registry": _registryCategoriesExpanded = !_registryCategoriesExpanded; break;
            case "Software": _softwareCategoriesExpanded = !_softwareCategoriesExpanded; break;
        }
        UpdateCategoryExpansionLabels();
        ScheduleCategoryLayoutUpdate();
    }

    private void UpdateCategoryExpansionLabels()
    {
        SystemCategoryToggle.Content = Localization.Get(_systemCategoriesExpanded ? "collapseCategories" : "expandCategories");
        RegistryCategoryToggle.Content = Localization.Get(_registryCategoriesExpanded ? "collapseCategories" : "expandCategories");
        SoftwareCategoryToggle.Content = Localization.Get(_softwareCategoriesExpanded ? "collapseCategories" : "expandCategories");
    }

    private void ScheduleCategoryLayoutUpdate() => Dispatcher.BeginInvoke(
        new Action(UpdateCategoryLayouts), DispatcherPriority.Loaded);

    private void UpdateCategoryLayouts()
    {
        UpdateCategoryLayout(SystemCategoryHost, SystemCategoryTabs, SystemCategoryToggle, _systemCategoriesExpanded);
        UpdateCategoryLayout(RegistryCategoryHost, RegistryCategoryTabs, RegistryCategoryToggle, _registryCategoriesExpanded);
        UpdateCategoryLayout(SoftwareCategoryHost, SoftwareCategoryTabs, SoftwareCategoryToggle, _softwareCategoriesExpanded);
    }

    private static void UpdateCategoryLayout(Border host, ItemsControl tabs, Button toggle, bool expanded)
    {
        host.MaxHeight = double.PositiveInfinity;
        var panel = FindVisualChildren<WrapPanel>(tabs).FirstOrDefault();
        if (panel is null || host.ActualWidth <= 0) return;
        panel.Measure(new Size(host.ActualWidth, double.PositiveInfinity));
        var hasOverflow = panel.DesiredSize.Height > CollapsedCategoryHeight + 1;
        toggle.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
        host.MaxHeight = hasOverflow && !expanded ? CollapsedCategoryHeight : double.PositiveInfinity;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private async void SelfTest_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;
        try
        {
            var compatibility = _compatibility.Read();
            ExecutionText.Text = Localization.Get("selfTestStarting");
            var executor = new RegisteredTaskExecutor(_catalog, new BridgeClient(), compatibility: compatibility);
            var progress = new Progress<SelfTestProgress>(value =>
                ExecutionText.Text = Localization.Format("selfTestProgress", value.Completed, value.Total, value.TaskId));
            var report = await new SelfTestRunner(_catalog, executor).RunAsync(compatibility, progress: progress);
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state", "self-tests");
            var paths = await SelfTestReportExporter.ExportBundleAsync(report, stateRoot, $"self-test-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
            ExecutionText.Text = Localization.Format("selfTestCompleted", report.Total, report.Ready, report.Satisfied, report.Unavailable, report.Failed);
            MessageBox.Show(this, Localization.Format("selfTestReportSaved", paths.HtmlPath), Localization.Get("selfTest"), MessageBoxButton.OK,
                report.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            ExecutionText.Text = Localization.Get("selfTestCancelled");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("selfTestFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private void ExecutionPaneToggle_Click(object sender, RoutedEventArgs e) => SetExecutionPaneExpanded(!_executionPaneExpanded);

    private void SetExecutionPaneExpanded(bool expanded)
    {
        _executionPaneExpanded = expanded;
        ExecutionPaneColumn.Width = expanded ? new GridLength(380) : new GridLength(56);
        ExecutionPaneHeader.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ExecutionPaneToggleButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ExecutionViewTabs.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PendingExecutionView.Visibility = expanded && !_showCompletedView ? Visibility.Visible : Visibility.Collapsed;
        CompletedExecutionView.Visibility = expanded && _showCompletedView ? Visibility.Visible : Visibility.Collapsed;
        CollapsedExecutionButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ExecutionView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string view }) return;
        ShowExecutionView(view.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    }

    private void ShowExecutionView(bool completed)
    {
        _showCompletedView = completed;
        PendingExecutionView.Visibility = completed ? Visibility.Collapsed : Visibility.Visible;
        CompletedExecutionView.Visibility = completed ? Visibility.Visible : Visibility.Collapsed;
        PendingViewButton.Background = (Brush)Application.Current.Resources[completed ? "PanelBrush" : "ActiveNavBrush"];
        PendingViewButton.BorderBrush = (Brush)Application.Current.Resources[completed ? "BorderBrush" : "ActiveNavBorderBrush"];
        CompletedViewButton.Background = (Brush)Application.Current.Resources[completed ? "ActiveNavBrush" : "PanelBrush"];
        CompletedViewButton.BorderBrush = (Brush)Application.Current.Resources[completed ? "ActiveNavBorderBrush" : "BorderBrush"];
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<WorkspaceArea>(tag, out var area)) Navigate(area);
    }

    private void Navigate(WorkspaceArea area)
    {
        _activeArea = area;
        WorkspaceTabs.SelectedItem = area switch { WorkspaceArea.System => SystemTab, WorkspaceArea.Registry => RegistryTab, WorkspaceArea.Software => SoftwareTab, WorkspaceArea.Recovery => RecoveryTab, _ => HomeTab };
        UpdateActiveNavigation();
        (PageTitleText.Text, PageSubtitleText.Text) = area switch
        {
            WorkspaceArea.System => (Localization.Get("system"), Localization.Get("settingsSubtitle")),
            WorkspaceArea.Registry => (Localization.Get("registryOptimization"), Localization.Get("registrySubtitle")),
            WorkspaceArea.Software => (Localization.Get("software"), Localization.Get("softwareSubtitle")),
            WorkspaceArea.Recovery => (Localization.Get("recoveryCenter"), Localization.Get("recoverySubtitle")),
            _ => (Localization.Get("home"), Localization.Get("safePlan"))
        };
        if (area == WorkspaceArea.Recovery) _ = RefreshRecoveryAsync();
        ApplyFilter();
    }

    private void UpdateActiveNavigation()
    {
        var active = (SolidColorBrush)Application.Current.Resources["ActiveNavBrush"];
        var activeBorder = (SolidColorBrush)Application.Current.Resources["ActiveNavBorderBrush"];
        var inactive = (SolidColorBrush)Application.Current.Resources["TransparentBrush"];
        var inactiveBorder = (SolidColorBrush)Application.Current.Resources["TransparentBrush"];
        foreach (var button in new[] { HomeNavButton, SystemNavButton, RegistryNavButton, SoftwareNavButton, RecoveryNavButton })
        {
            var selected = button.Tag?.ToString() == _activeArea.ToString();
            button.Background = selected ? active : inactive;
            button.BorderBrush = selected ? activeBorder : inactiveBorder;
            button.BorderThickness = selected ? new Thickness(1) : new Thickness(0);
            button.Foreground = selected ? (SolidColorBrush)Application.Current.Resources["AccentBrush"] : (SolidColorBrush)Application.Current.Resources["AppTextBrush"];
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private async Task ReloadCatalogAsync()
    {
        var compatibility = _compatibility.Read();
        var installer = new ControlledExtensionInstaller(ExtensionTrustPolicy.CreateValidator());
        var installed = await installer.ListAsync(ExtensionPaths.UserRoot, compatibility.CurrentBuild,
            compatibility.OsArchitecture, CancellationToken.None);
        _catalog = new Catalog(installed);
        LoadCatalogRows();
        ApplyFilter();
    }

    private void LoadCatalogRows()
    {
        _allRows.Clear();
        foreach (var task in _catalog.GetTasks())
        {
            var row = new TaskRow(task, _catalog);
            _allRows.Add(row);
        }
        RefreshCategoryTabs();
        RefreshTaskPlanStates();
        SyncExecutionQueueWithDraft();
    }

    private void RefreshCategoryTabs()
    {
        _systemCategory = RefreshCategoryTabs(
            SystemCategoryTabs,
            _allRows.Where(row => !IsSoftwareTask(row)),
            _systemCategory,
            "allSettingsCategories");
        _registryCategory = RefreshCategoryTabs(
            RegistryCategoryTabs,
            _allRows.Where(row => row.Kind == TaskKind.RegistrySetting),
            _registryCategory,
            "allRegistryCategories");
        _softwareCategory = RefreshCategoryTabs(
            SoftwareCategoryTabs,
            _allRows.Where(IsSoftwareTask),
            _softwareCategory,
            "allSoftwareCategories");
        ScheduleCategoryLayoutUpdate();
    }

    private static string RefreshCategoryTabs(ItemsControl host, IEnumerable<TaskRow> rows, string selectedKey, string allLabelKey)
    {
        var keys = rows.Select(row => row.RawDomain).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CatalogLocalization.Domain, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (!keys.Contains(selectedKey, StringComparer.OrdinalIgnoreCase)) selectedKey = string.Empty;

        var categories = keys.Select(key => new LocalizedCategory(key, CatalogLocalization.Domain(key), key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase))).ToList();
        categories.Insert(0, new LocalizedCategory(string.Empty, Localization.Get(allLabelKey), string.IsNullOrEmpty(selectedKey)));
        host.ItemsSource = categories;
        return selectedKey;
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (_planLocked || sender is not FrameworkElement { Tag: string taskId }) return;
        var task = _allRows.FirstOrDefault(item => item.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null || !task.CanAdd) return;

        var descriptor = _catalog.Find(taskId);
        if (descriptor is null) return;
        var selectedIds = _draftItems.Select(item => item.TaskId).ToArray();
        var conflicts = TaskRelationPlanner.Conflicts(descriptor, selectedIds);
        if (!conflicts.IsDefaultOrEmpty)
        {
            MessageBox.Show(Localization.Format("taskConflict", string.Join(", ", conflicts.Select(item => CatalogTaskName(item.TargetTaskId)))),
                Localization.Get("dependencyReview"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var closure = TaskRelationPlanner.RequiredClosure(_catalog, taskId);
        var required = closure.Where(item => !item.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase) &&
                                             !selectedIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (required.Any(item => !item.IsAvailable))
        {
            MessageBox.Show(Localization.Format("dependencyUnavailable", string.Join(", ", required.Where(item => !item.IsAvailable).Select(item => CatalogLocalization.TaskName(item.Id, item.DisplayName)))),
                Localization.Get("dependencyReview"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (required.Length > 0 && MessageBox.Show(Localization.Format("confirmRequiredTasks", string.Join(", ", required.Select(item => CatalogLocalization.TaskName(item.Id, item.DisplayName)))),
                Localization.Get("dependencyReview"), MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        var additions = new List<DraftPlanItem>();
        foreach (var item in required.Append(descriptor))
        {
            InteractiveConfiguration? configuration = null;
            if (item.Id == taskId && item.Kind == TaskKind.Winget)
                configuration = new InteractiveConfiguration(ImmutableDictionary<string, string>.Empty.Add(item.Id, task.SoftwareAction), null);
            if (ConfigurationDialog.RequiresInput(item.Id) && !TryConfigureTask(item.Id, null, out configuration)) return;
            if (!ValidateDraftConfiguration(configuration, null)) return;
            additions.Add(new DraftPlanItem(Guid.NewGuid().ToString("N"), item.Id, configuration));
        }
        _draftItems.AddRange(additions);
        RefreshTaskPlanStates();
        SyncExecutionQueueWithDraft();
        var recommendations = TaskRelationPlanner.Recommendations(descriptor)
            .Where(item => !_draftItems.Any(draft => draft.TaskId.Equals(item.TargetTaskId, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (recommendations.Length > 0)
            ExecutionText.Text = Localization.Format("recommendedTasks", string.Join(", ", recommendations.Select(item => CatalogTaskName(item.TargetTaskId))));
    }

    private bool TryConfigureTask(string taskId, InteractiveConfiguration? existing, out InteractiveConfiguration? configuration)
    {
        var dialog = new ConfigurationDialog([taskId], existing) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            configuration = null;
            return false;
        }
        configuration = dialog.Result;
        return true;
    }

    private void EditQueueItem_Click(object sender, RoutedEventArgs e)
    {
        if (_planLocked) return;
        if (sender is not FrameworkElement { Tag: string instanceId }) return;
        var index = _draftItems.FindIndex(item => item.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || !ConfigurationDialog.RequiresInput(_draftItems[index].TaskId)) return;
        var item = _draftItems[index];
        if (!TryConfigureTask(item.TaskId, item.Configuration, out var configuration)) return;
        if (!ValidateDraftConfiguration(configuration, item.InstanceId)) return;
        _draftItems[index] = item with { Configuration = configuration };
        SyncExecutionQueueWithDraft();
    }

    private bool ValidateDraftConfiguration(InteractiveConfiguration? configuration, string? excludedInstanceId)
    {
        if (configuration?.Accounts is not { IsDefaultOrEmpty: false } accounts) return true;
        var existingNames = _draftItems
            .Where(item => !item.InstanceId.Equals(excludedInstanceId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Configuration?.Accounts ?? [])
            .Select(account => account.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!accounts.Any(account => existingNames.Contains(account.Name))) return true;
        MessageBox.Show(Localization.Get("duplicateAccount"), Localization.Get("invalidValue"), MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void RemoveQueueItem_Click(object sender, RoutedEventArgs e)
    {
        if (_planLocked) return;
        if (sender is not FrameworkElement { Tag: string instanceId }) return;
        var selected = _draftItems.FirstOrDefault(item => item.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase));
        if (selected is null) return;
        if (_draftItems.Count(item => item.TaskId.Equals(selected.TaskId, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            _draftItems.RemoveAll(item => item.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase));
            RefreshTaskPlanStates();
            SyncExecutionQueueWithDraft();
            return;
        }
        var removeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selected.TaskId };
        var pending = new Queue<string>();
        pending.Enqueue(selected.TaskId);
        while (pending.TryDequeue(out var dependencyId))
            foreach (var dependent in TaskRelationPlanner.Dependents(_catalog, _draftItems.Select(item => item.TaskId), dependencyId))
                if (removeIds.Add(dependent.Id)) pending.Enqueue(dependent.Id);
        var dependentNames = removeIds.Where(id => !id.Equals(selected.TaskId, StringComparison.OrdinalIgnoreCase)).Select(CatalogTaskName);
        if (removeIds.Count > 1 && MessageBox.Show(Localization.Format("confirmDependentRemoval", string.Join(", ", dependentNames)),
                Localization.Get("dependencyReview"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var removed = _draftItems.RemoveAll(item => removeIds.Contains(item.TaskId));
        if (removed == 0) return;
        RefreshTaskPlanStates();
        SyncExecutionQueueWithDraft();
    }

    private string CatalogTaskName(string taskId)
    {
        var task = _catalog.Find(taskId);
        return task is null ? taskId : CatalogLocalization.TaskName(task.Id, task.DisplayName);
    }

    private void TaskDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string taskId }) return;
        var descriptor = _catalog.Find(taskId);
        var row = _allRows.FirstOrDefault(item => item.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || row is null) return;

        var dialog = new TaskDetailsDialog(descriptor, row.CurrentState, BuildDraftSummary(taskId), CatalogTaskName)
        {
            Owner = this,
            FlowDirection = FlowDirection
        };
        dialog.ShowDialog();
    }

    private async void RefreshSoftwareStatus_Click(object sender, RoutedEventArgs e)
    {
        var softwareRows = _allRows.Where(IsSoftwareTask).ToArray();
        foreach (var row in softwareRows) row.SetSoftwareInventoryPending();
        SoftwareSelectionText.Text = Localization.Get("softwareStatusDetecting");
        if (sender is Button refreshButton) refreshButton.IsEnabled = false;
        try
        {
            var snapshot = await new SoftwareInventoryService(_catalog).ReadAsync();
            foreach (var row in softwareRows)
                row.SetSoftwareInventory(snapshot.Items.GetValueOrDefault(row.Id)?.Status ?? SoftwareInventoryStatus.DetectionFailed);
            ApplyFilter();
            SoftwareSelectionText.Text = snapshot.Error is null
                ? Localization.Format("softwareInventoryUpdated", snapshot.CheckedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))
                : Localization.Format("softwareInventoryFailed", snapshot.Error);
        }
        finally
        {
            if (sender is Button completedButton) completedButton.IsEnabled = true;
        }
    }

    private void OpenInstalledApps_Click(object sender, RoutedEventArgs e) => OpenSystemTarget("ms-settings:appsfeatures");

    private void OpenWindowsUpdate_Click(object sender, RoutedEventArgs e) => OpenSystemTarget("ms-settings:windowsupdate");

    private void OpenPowerSettings_Click(object sender, RoutedEventArgs e) => OpenSystemTarget("ms-settings:powersleep");

    private void OpenSystemProtection_Click(object sender, RoutedEventArgs e) => OpenSystemTarget("SystemPropertiesProtection.exe");

    private void OpenSystemRestore_Click(object sender, RoutedEventArgs e) => OpenSystemTarget("rstrui.exe");

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(StateRoot(), "registry-backups");
        Directory.CreateDirectory(folder);
        OpenSystemTarget(folder);
    }

    private async void RefreshRecovery_Click(object sender, RoutedEventArgs e) => await RefreshRecoveryAsync();

    private async Task RefreshRecoveryAsync()
    {
        try
        {
            var runs = await RegistryBackupCatalogReader.ListRunsAsync(StateRoot());
            _recoveryRuns.Clear();
            foreach (var run in runs) _recoveryRuns.Add(new RecoveryRunRow(run));
            _recoveryRecords.Clear();
            RecoverySummaryText.Text = Localization.Format("recoveryRunCount", runs.Count);
            if (_recoveryRuns.Count > 0) RecoveryRunsGrid.SelectedIndex = 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RecoverySummaryText.Text = Localization.Format("recoveryLoadFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private async void RecoveryRunsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _recoveryRecords.Clear();
        if (RecoveryRunsGrid.SelectedItem is not RecoveryRunRow selected) return;
        try
        {
            var records = await RegistryBackupCatalogReader.LoadRunAsync(StateRoot(), selected.RunId);
            foreach (var record in records) _recoveryRecords.Add(new RecoveryRecordRow(record, _catalog));
            RecoverySummaryText.Text = Localization.Format("recoverySelectionSummary", records.Count, selected.RunId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RecoverySummaryText.Text = Localization.Format("recoveryLoadFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private async void ExportRecoveryRollback_Click(object sender, RoutedEventArgs e)
    {
        if (RecoveryRunsGrid.SelectedItem is not RecoveryRunRow selected)
        {
            RecoverySummaryText.Text = Localization.Get("selectRecoveryRun");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = Localization.Get("exportSelectedRollback"),
            Filter = Localization.Get("registryFileFilter"),
            FileName = $"WISK-rollback-{selected.RunId[..Math.Min(8, selected.RunId.Length)]}.reg",
            AddExtension = true,
            DefaultExt = ".reg"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var count = await RegistryBackupExporter.ExportRunAsync(StateRoot(), selected.RunId, dialog.FileName);
            RecoverySummaryText.Text = Localization.Format("registryRollbackExported", count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RecoverySummaryText.Text = Localization.Format("recoveryLoadFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private static string StateRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");

    private void OpenSystemTarget(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("systemPageOpenFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private void RefreshTaskPlanStates()
    {
        foreach (var row in _allRows)
            row.SetPlanCount(_draftItems.Count(item => item.TaskId.Equals(row.Id, StringComparison.OrdinalIgnoreCase)));
        UpdateSelectionSummary();
    }

    private void SyncExecutionQueueWithDraft()
    {
        if (_runCancellation is not null) return;

        var existing = _executionRows.ToDictionary(row => row.InstanceId, StringComparer.OrdinalIgnoreCase);
        _executionRows.Clear();
        for (var index = 0; index < _draftItems.Count; index++)
        {
            var draft = _draftItems[index];
            var task = _allRows.First(row => row.Id.Equals(draft.TaskId, StringComparison.OrdinalIgnoreCase));
            var row = existing.TryGetValue(draft.InstanceId, out var current)
                ? current
                : new ExecutionRow(index + 1, draft.InstanceId, task.Id, task.DisplayName, BuildDraftSummary(draft));
            row.SetConfigurationSummary(BuildDraftSummary(draft));
            row.SetOrder(index + 1);
            _executionRows.Add(row);
        }

        RefreshExecutionPaneText();
        ApplyButton.IsEnabled = !_planLocked && _draftItems.Count > 0;
        ResumeButton.IsEnabled = !_planLocked && _draftItems.Count == 0;
        ResumeCompletedButton.IsEnabled = !_planLocked && _draftItems.Count == 0;
    }

    private string BuildDraftSummary(DraftPlanItem draft) => BuildDraftSummary(draft.TaskId, draft.Configuration);

    private string BuildDraftSummary(string taskId, InteractiveConfiguration? configuration = null)
    {
        var registryEntries = RegistryOptimizationCatalog.GetTaskEntries(taskId);
        if (!registryEntries.IsDefaultOrEmpty)
        {
            if (registryEntries.Length > 1)
                return string.Format(Localization.Get("registryValueCount"), registryEntries.Length);
            var registry = registryEntries[0];
            var value = registry.Operation == RegistryOperationKind.DeleteValue ? Localization.Get("deleteValue") : registry.RawValue;
            if (value.Length > 48) value = value[..48] + "…";
            return $"{registry.Hive} · {registry.ValueName} · {value}";
        }
        configuration ??= _draftItems.FirstOrDefault(item => item.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase))?.Configuration;
        if (configuration is null) return string.Empty;
        if (configuration.Accounts is { IsDefaultOrEmpty: false } accounts)
            return string.Join(", ", accounts.Select(account => account.Name));
        var valueSummary = configuration.Parameters.Values.FirstOrDefault() ?? string.Empty;
        if (taskId == "setting-explorer-this-pc")
            return valueSummary switch
            {
                "enabled" => Localization.Get("explorerThisPc"),
                "disabled" => Localization.Get("explorerHome"),
                "default" => Localization.Get("registrySettingDefault"),
                _ => valueSummary
            };
        return valueSummary switch
        {
            "enabled" => Localization.Get("registrySettingEnabled"),
            "disabled" => Localization.Get("registrySettingDisabled"),
            "default" => Localization.Get("registrySettingDefault"),
            "install" => Localization.Get("installApp"),
            "upgrade" => Localization.Get("upgradeApp"),
            _ => valueSummary
        };
    }

    private void RefreshExecutionPaneText()
    {
        if (QueueCountText is null || RunSummaryText is null) return;
        QueueCountText.Text = string.Format(Localization.Get("queueCount"), _executionRows.Count);
        RefreshPlanImpact();
        if (_executionRows.Any(row => row.State != TaskState.Pending)) return;
        RunSummaryText.Text = _executionRows.Count == 0
            ? Localization.Get("selectToPlan")
            : string.Format(Localization.Get("queueReady"), _executionRows.Count);
    }

    private void RefreshPlanImpact()
    {
        if (PlanImpactText is null || RestorePointRecommendationText is null || RestorePointRecommendationButton is null) return;
        var descriptors = _draftItems.Select(item => _catalog.Find(item.TaskId)).OfType<TaskDescriptor>().ToArray();
        if (descriptors.Length == 0)
        {
            PlanImpactText.Text = Localization.Get("noPlanImpact");
            RestorePointRecommendationText.Visibility = Visibility.Collapsed;
            RestorePointRecommendationButton.Visibility = Visibility.Collapsed;
            return;
        }

        var impact = PlanImpactAnalyzer.Analyze(descriptors);
        PlanImpactText.Text = Localization.Format("planImpactSummary", impact.AdministratorCount, impact.HighRiskCount,
            impact.InternetCount, impact.RebootCount, impact.SignOutCount, impact.DependencyCount,
            impact.RecommendationCount, impact.ResourceLocks.Length, impact.ExactRollbackCount,
            impact.ConditionalRollbackCount, impact.ManualRollbackCount);
        RestorePointRecommendationText.Visibility = impact.RestorePointRecommended ? Visibility.Visible : Visibility.Collapsed;
        RestorePointRecommendationButton.Visibility = impact.RestorePointRecommended ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetPlanLocked(bool locked)
    {
        _planLocked = locked;
        foreach (var row in _allRows) row.SetPlanLocked(locked);
        ExecutionGrid.IsEnabled = !locked;
        AllowElevatedCheck.IsEnabled = !locked;
        AllowHighRiskCheck.IsEnabled = !locked;
        ApplyButton.IsEnabled = !locked && _draftItems.Count > 0;
        ResumeButton.IsEnabled = !locked && _draftItems.Count == 0;
        ResumeCompletedButton.IsEnabled = !locked && _draftItems.Count == 0;
        CancelButton.IsEnabled = locked;
        RunSummaryText.Text = locked ? Localization.Get("planLockedDuringRun") : RunSummaryText.Text;
    }

    private async Task FinalizeCompletedRunAsync(RunStateSnapshot snapshot)
    {
        _draftItems.Clear();
        _draftPolicy = new ExecutionPolicy();
        _executionRows.Clear();
        RefreshTaskPlanStates();
        RefreshExecutionPaneText();
        ExecutionText.Text = RunResultText(snapshot);
        await RefreshCompletedRunsAsync();
        ShowExecutionView(true);
    }

    private async Task RefreshCompletedRunsAsync()
    {
        var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
        var history = await new AtomicJsonStateStore(stateRoot).ListAsync(CancellationToken.None);
        var historyVerifier = new ExecutionHistoryVerifier(new WindowsTaskExecutor(new BridgeClient(), _catalog));
        _completedRuns.Clear();
        foreach (var snapshot in history)
        {
            var verification = await historyVerifier.VerifyAsync(snapshot, CancellationToken.None);
            _completedRuns.Add(new CompletedRunRow(snapshot, verification));
        }
        CompletedViewButton.Content = Localization.Format("completedRunsCount", _completedRuns.Count);
        _completedTaskRows.Clear();
        CompletedTaskDetailsText.Text = Localization.Get("selectCompletedTask");
    }

    private void CompletedRunsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _completedTaskRows.Clear();
        if (CompletedRunsGrid.SelectedItem is CompletedRunRow run)
            foreach (var result in run.Snapshot.Results) _completedTaskRows.Add(new CompletedTaskRow(run.ResultFor(result), _catalog));
        CompletedTaskDetailsText.Text = Localization.Get("selectCompletedTask");
    }

    private void CompletedTasksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CompletedTaskDetailsText.Text = CompletedTasksGrid.SelectedItem is CompletedTaskRow row
            ? row.Details
            : Localization.Get("selectCompletedTask");
    }

    private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshCompletedRunsAsync(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ExecutionText.Text = Localization.Format("runHistoryUnavailable", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private async void CleanupHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;
        try
        {
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var store = new AtomicJsonStateStore(stateRoot);
            var policy = HistoryRetentionPolicy.Default;
            var preview = await store.CleanupAsync(policy, dryRun: true);
            if (preview.Deleted == 0)
            {
                MessageBox.Show(Localization.Get("cleanupHistoryNone"), Localization.Get("cleanupHistory"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(Localization.Format("cleanupHistoryConfirm", preview.Deleted, policy.MaxRuns, (int)policy.EffectiveMaxAge.TotalDays),
                    Localization.Get("cleanupHistory"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var result = await store.CleanupAsync(policy);
            await RefreshCompletedRunsAsync();
            ExecutionText.Text = Localization.Format("cleanupHistoryCompleted", result.Deleted, result.Retained, result.Protected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            ExecutionText.Text = Localization.Format("cleanupHistoryFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private void CompletedRunDetails_Click(object sender, RoutedEventArgs e)
    {
        if (CompletedRunsGrid.SelectedItem is not CompletedRunRow row)
        {
            MessageBox.Show(Localization.Get("selectCompletedRun"), Localization.Get("completedRuns"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = new StringBuilder($"{row.Title}\n{row.RunId}\n\n");
        foreach (var result in row.Snapshot.Results)
        {
            var displayResult = row.ResultFor(result);
            text.AppendLine($"{CatalogLocalization.TaskName(displayResult.TaskId, _catalog.Find(displayResult.TaskId)?.DisplayName ?? displayResult.TaskId)}  ·  {LocalizedState(displayResult.State)}\n{displayResult.Code} · {SensitiveDataRedactor.Redact(displayResult.Message)}\n{Localization.Get("verificationStatus")}: {LocalizedVerification(displayResult.VerificationStatus)} · {Localization.Get("rebootRequired")}: {Localization.Get(displayResult.RebootRequired ? "yes" : "no")}\n{displayResult.StartedAt?.ToLocalTime():g} - {displayResult.CompletedAt?.ToLocalTime():g}\n");
        }
        MessageBox.Show(text.ToString(), Localization.Get("completedBatch"), MessageBoxButton.OK,
            row.Snapshot.State == TaskState.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private async void RetryFailedTask_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null || sender is not FrameworkElement { Tag: string taskId }) return;
        if (_draftItems.Count > 0)
        {
            MessageBox.Show(Localization.Get("retryDraftExists"), Localization.Get("retryTask"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (CompletedRunsGrid.SelectedItem is not CompletedRunRow run) return;
        if (taskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Localization.Get("retryRequiresReconfiguration"), Localization.Get("retryTask"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var compatibility = _compatibility.Read();
            if (!compatibility.IsApplySupported) { ExecutionText.Text = Localization.Get("applyUnavailable"); return; }
            if (!ConfirmRiskAuthorization([taskId])) return;
            var plan = SingleTaskRetryPlan.Create(run.Snapshot, taskId);
            PrepareExecution(plan, Localization.Format("retryStarting", CatalogLocalization.TaskName(taskId, _catalog.Find(taskId)?.DisplayName ?? taskId)));
            _runCancellation = new CancellationTokenSource();
            SetPlanLocked(true);
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var engine = new ExecutionEngine(new RegisteredTaskExecutor(_catalog, new BridgeClient(), compatibility: compatibility), new AtomicJsonStateStore(stateRoot));
            var progress = new Progress<ExecutionProgress>(UpdateExecutionProgress);
            var snapshot = await new RunCoordinator(engine, stateRoot).ExecuteAsync(plan, plan.Policy ?? new ExecutionPolicy(), compatibility.IsAdministrator, _runCancellation.Token, progress: progress);
            await FinalizeCompletedRunAsync(snapshot);
        }
        catch (PlanValidationException exception)
        {
            ExecutionText.Text = Localization.Format("retryUnavailable", SensitiveDataRedactor.Redact(exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("runFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetPlanLocked(false);
        }
    }

    private async void InstallExtension_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Localization.Get("installExtensionTitle"), Filter = Localization.Get("extensionPackageFilter"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var compatibility = _compatibility.Read();
            var installer = new ControlledExtensionInstaller(ExtensionTrustPolicy.CreateValidator());
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var installed = await installer.InstallAsync(Path.GetFullPath(dialog.FileName), ExtensionPaths.UserRoot,
                [AppContext.BaseDirectory, stateRoot], compatibility.CurrentBuild, compatibility.OsArchitecture, CancellationToken.None);
            await ReloadCatalogAsync();
            ExecutionText.Text = Localization.Format("extensionInstalled", installed.PackageId, installed.Version);
        }
        catch (ExtensionInstallException exception)
        {
            ExecutionText.Text = Localization.Format("extensionInstallFailedWithCode", exception.Code, SensitiveDataRedactor.Redact(exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("extensionInstallFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;
        var compatibility = _compatibility.Read();
        if (!compatibility.IsApplySupported)
        {
            ExecutionText.Text = Localization.Get("applyUnavailable");
            return;
        }
        var ids = _draftItems.Select(item => item.TaskId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0)
        {
            ExecutionText.Text = Localization.Get("chooseTasksBeforeApply");
            Navigate(WorkspaceArea.System);
            return;
        }
        if (!ConfirmRiskAuthorization(ids)) return;
        try
        {
            var profile = BuildInteractiveProfile(ids);
            if (profile is null) { ExecutionText.Text = Localization.Get("configurationCancelled"); return; }
            if (!compatibility.IsAdministrator && profile.Accounts is { IsDefaultOrEmpty: false })
            {
                ExecutionText.Text = Localization.Get("accountChangesRequireAdmin");
                return;
            }
            var plan = new PlanBuilder(_catalog).Build(profile, compatibility);
            var preflight = BuildPreflightReport(plan, compatibility);
            var preflightDialog = new PreflightDialog(preflight, allowContinue: true) { Owner = this };
            if (preflightDialog.ShowDialog() != true)
            {
                ExecutionText.Text = preflight.CanExecute ? Localization.Get("executionCancelledAfterPreflight") : Localization.Get("preflightBlocked");
                return;
            }
            PrepareExecution(plan, Localization.Get(compatibility.IsAdministrator ? "startingRun" : "preparingElevation"));
            if (!compatibility.IsAdministrator)
            {
                var uacStateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
                var envelopePath = UacPlanHandoff.WriteApplyEnvelope(plan, uacStateRoot);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable."),
                        Arguments = "--uac-envelope \"" + envelopePath + "\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = AppContext.BaseDirectory
                    });
                    ExecutionText.Text = Localization.Get("elevationRequestedApply");
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    ExecutionText.Text = Localization.Format("elevationDeclined", SensitiveDataRedactor.Redact(exception.Message));
                    try { File.Delete(envelopePath); } catch { }
                }
                return;
            }
            _runCancellation = new CancellationTokenSource();
            SetPlanLocked(true);
            ExecutionText.Text = Localization.Format("runningTasks", plan.Tasks.Length);
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var engine = new ExecutionEngine(new RegisteredTaskExecutor(_catalog, new BridgeClient(), compatibility: compatibility), new AtomicJsonStateStore(stateRoot));
            var progress = new Progress<ExecutionProgress>(UpdateExecutionProgress);
            var snapshot = await new RunCoordinator(engine, stateRoot).ExecuteAsync(plan, plan.Policy ?? profile.Policy, compatibility.IsAdministrator, _runCancellation.Token, profile.Accounts, progress);
            await FinalizeCompletedRunAsync(snapshot);
        }
        catch (OperationCanceledException)
        {
            ExecutionText.Text = Localization.Get("runCancelledHistory");
        }
        catch (PlanValidationException exception)
        {
            ExecutionText.Text = Localization.Format("planValidationFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("runFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetPlanLocked(false);
        }
    }

    private bool ConfirmRiskAuthorization(IReadOnlyCollection<string> ids)
    {
        var selected = ids.Select(id => _catalog.Find(id)).Where(task => task is not null).ToArray();
        var elevatedCount = selected.Count(task => task!.Risk == RiskLevel.Elevated);
        var highRiskCount = selected.Count(task => task!.Risk == RiskLevel.High);
        var authorizeElevated = elevatedCount > 0 && AllowElevatedCheck.IsChecked != true;
        var authorizeHighRisk = highRiskCount > 0 && AllowHighRiskCheck.IsChecked != true;

        if (authorizeElevated && MessageBox.Show(this,
                Localization.Format("confirmElevatedAuthorization", elevatedCount),
                Localization.Get("executionAuthorization"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            ExecutionText.Text = Localization.Get("authorizationDeclined");
            return false;
        }

        if (authorizeHighRisk && MessageBox.Show(this,
                Localization.Format("confirmHighRiskAuthorization", highRiskCount),
                Localization.Get("executionAuthorization"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            ExecutionText.Text = Localization.Get("authorizationDeclined");
            return false;
        }

        if (authorizeElevated) AllowElevatedCheck.IsChecked = true;
        if (authorizeHighRisk) AllowHighRiskCheck.IsChecked = true;
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _runCancellation?.Cancel();

    private void Preflight_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildAnalysisPlan(out var plan, out var compatibility)) return;
        var dialog = new PreflightDialog(BuildPreflightReport(plan, compatibility), allowContinue: false) { Owner = this };
        dialog.ShowDialog();
    }

    private async void SimulatePlan_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null || !TryBuildAnalysisPlan(out var plan, out _)) return;
        _runCancellation = new CancellationTokenSource();
        SetPlanLocked(true);
        PrepareExecution(plan, Localization.Get("simulationStarting"));
        try
        {
            var progress = new Progress<ExecutionProgress>(UpdateExecutionProgress);
            var snapshot = await new ExecutionEngine(new SimulationTaskExecutor()).ExecuteAsync(
                plan, plan.Policy ?? new ExecutionPolicy(), true, _runCancellation.Token, progress: progress);
            DisplayRunSnapshot(plan, snapshot);
            ExecutionText.Text = Localization.Format("simulationComplete", snapshot.Results.Length,
                snapshot.Results.Count(result => result.State is TaskState.Succeeded or TaskState.Skipped));
        }
        catch (OperationCanceledException)
        {
            ExecutionText.Text = Localization.Get("simulationCancelled");
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            SetPlanLocked(false);
        }
    }

    private bool TryBuildAnalysisPlan(out ImmutablePlan plan, out CompatibilitySnapshot compatibility)
    {
        plan = default!;
        compatibility = _compatibility.Read();
        var ids = _draftItems.Select(item => item.TaskId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0)
        {
            ExecutionText.Text = Localization.Get("chooseTasksBeforeApply");
            return false;
        }
        try
        {
            var profile = BuildInteractiveProfile(ids);
            if (profile is null)
            {
                ExecutionText.Text = Localization.Get("configurationCancelled");
                return false;
            }
            var selected = ids.Select(id => _catalog.Find(id)).OfType<TaskDescriptor>().ToArray();
            profile = profile with
            {
                AllowElevated = selected.Any(task => task.Risk == RiskLevel.Elevated),
                AllowHighRisk = selected.Any(task => task.Risk == RiskLevel.High)
            };
            plan = new PlanBuilder(_catalog).Build(profile);
            return true;
        }
        catch (PlanValidationException exception)
        {
            ExecutionText.Text = Localization.Format("planValidationFailed", SensitiveDataRedactor.Redact(exception.Message));
            return false;
        }
    }

    private ExecutionPreflightReport BuildPreflightReport(ImmutablePlan plan, CompatibilitySnapshot compatibility)
    {
        var descriptors = plan.Tasks.Select(task => _catalog.Find(task.TaskId)).OfType<TaskDescriptor>();
        return ExecutionPreflightEvaluator.Evaluate(plan, descriptors, compatibility, ExecutionEnvironmentProbe.Read());
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;
        var compatibility = _compatibility.Read();
        if (!compatibility.IsApplySupported)
        {
            ExecutionText.Text = Localization.Get("resumeUnavailable");
            return;
        }

        var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
        try
        {
            var store = new AtomicJsonStateStore(stateRoot);
            var candidate = (await store.ListAsync(CancellationToken.None)).FirstOrDefault(snapshot =>
                snapshot.State is TaskState.Failed or TaskState.Cancelled or TaskState.NeedsManualReview or TaskState.RecoveryRequired);
            if (candidate is null)
            {
                ExecutionText.Text = Localization.Get("noResumableRun");
                return;
            }
            if (string.IsNullOrWhiteSpace(candidate.SerializedPlan))
            {
                ExecutionText.Text = Localization.Get("resumePlanMissing");
                return;
            }

            var plan = JsonSerializer.Deserialize<ImmutablePlan>(candidate.SerializedPlan, PlanJsonOptions)
                ?? throw new PlanValidationException("Persisted run plan is invalid.", ErrorCode.StateCorrupt);
            PlanBuilder.ValidatePlanIntegrity(plan);
            PrepareExecution(plan, Localization.Format("preparingResume", candidate.RunId));
            if (!compatibility.IsAdministrator)
            {
                var envelopePath = UacPlanHandoff.WriteApplyEnvelope(plan, stateRoot, candidate.RunId);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable."),
                        Arguments = "--uac-envelope \"" + envelopePath + "\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WorkingDirectory = AppContext.BaseDirectory
                    });
                    ExecutionText.Text = Localization.Get("elevationRequestedResume");
                }
                catch (System.ComponentModel.Win32Exception exception)
                {
                    ExecutionText.Text = Localization.Format("elevationDeclined", SensitiveDataRedactor.Redact(exception.Message));
                    try { File.Delete(envelopePath); } catch { }
                }
                return;
            }
            _runCancellation = new CancellationTokenSource();
            SetPlanLocked(true);
            ExecutionText.Text = Localization.Format("resumingRun", candidate.RunId);
            var engine = new ExecutionEngine(new RegisteredTaskExecutor(_catalog, new BridgeClient(), compatibility: compatibility), store);
            var progress = new Progress<ExecutionProgress>(UpdateExecutionProgress);
            var snapshot = await new RunCoordinator(engine, stateRoot).ResumeAsync(candidate.RunId, plan, plan.Policy ?? new ExecutionPolicy(), compatibility.IsAdministrator, _runCancellation.Token, progress: progress);
            await FinalizeCompletedRunAsync(snapshot);
        }
        catch (PlanValidationException exception)
        {
            ExecutionText.Text = Localization.Format("planValidationFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("resumeFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
        finally
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetPlanLocked(false);
        }
    }

    private async void UacEnvelope_Loaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= UacEnvelope_Loaded;
        if (string.IsNullOrWhiteSpace(_uacEnvelopePath)) return;
        try
        {
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var envelope = UacPlanHandoff.ReadApplyEnvelope(_uacEnvelopePath, stateRoot);
            var plan = UacPlanHandoff.Validate(envelope.Plan);
            var compatibility = _compatibility.Read();
            if (!compatibility.IsAdministrator || !compatibility.IsApplySupported)
            {
                ExecutionText.Text = Localization.Get("elevatedCompatibilityFailed");
                return;
            }
            _runCancellation = new CancellationTokenSource();
            PrepareExecution(plan, envelope.ResumeRunId is null ? Localization.Get("startingElevatedRun") : Localization.Format("resumingElevatedRun", envelope.ResumeRunId));
            SetPlanLocked(true);
            ExecutionText.Text = Localization.Format("runningElevatedPlan", plan.PlanId);
            var engine = new ExecutionEngine(new RegisteredTaskExecutor(_catalog, new BridgeClient(), compatibility: compatibility), new AtomicJsonStateStore(stateRoot));
            var progress = new Progress<ExecutionProgress>(UpdateExecutionProgress);
            var coordinator = new RunCoordinator(engine, stateRoot);
            var snapshot = envelope.ResumeRunId is null
                ? await coordinator.ExecuteAsync(plan, plan.Policy ?? new ExecutionPolicy(), true, _runCancellation.Token, progress: progress)
                : await coordinator.ResumeAsync(envelope.ResumeRunId, plan, plan.Policy ?? new ExecutionPolicy(), true, _runCancellation.Token, progress: progress);
            await FinalizeCompletedRunAsync(snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Format("elevatedRunFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
        finally
        {
            try { File.Delete(_uacEnvelopePath); } catch { }
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetPlanLocked(false);
        }
    }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var history = await new AtomicJsonStateStore(root).ListAsync(CancellationToken.None);
            var text = new StringBuilder(Localization.Get("recentRuns") + "\n\n");
            if (history.Count == 0) text.Append(Localization.Get("noPersistedRuns"));
            foreach (var run in history.Take(10))
                text.AppendLine($"{run.UpdatedAt:yyyy-MM-dd HH:mm:ss zzz}  {run.RunId}  {LocalizedState(run.State)}{(run.RecoveryRequired ? "  " + Localization.Get("stateRecoveryRequired") : string.Empty)}");
            MessageBox.Show(text.ToString(), Localization.Get("runHistory"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Localization.Format("runHistoryUnavailable", SensitiveDataRedactor.Redact(exception.Message)), Localization.Get("runHistory"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var latest = (await new AtomicJsonStateStore(root).ListAsync(CancellationToken.None)).FirstOrDefault();
            if (latest is null)
            {
                MessageBox.Show(Localization.Get("noRunToExport"), Localization.Get("diagnostics"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var confirmation = MessageBox.Show(
                Localization.Get("diagnosticExportContents"),
                Localization.Get("diagnosticExportTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (confirmation != MessageBoxResult.OK) return;
            var dialog = new SaveFileDialog
            {
                Title = Localization.Get("exportRedactedDiagnostics"),
                Filter = Localization.Get("jsonFileFilter"),
                FileName = $"WISK wisk-{latest.RunId}.diagnostics.json",
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(this) != true) return;
            await DiagnosticExporter.ExportAsync(latest, Path.GetFullPath(dialog.FileName));
            ExecutionText.Text = Localization.Get("diagnosticsExported");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ExecutionText.Text = Localization.Format("diagnosticsExportFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private async void ExportPlanTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;
        if (_draftItems.Count == 0)
        {
            MessageBox.Show(Localization.Get("chooseTasksBeforeExport"), Localization.Get("exportPlanTemplate"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var templateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "templates");
        Directory.CreateDirectory(templateDirectory);
        var dialog = new SaveFileDialog
        {
            Title = Localization.Get("exportPlanTemplate"),
            Filter = Localization.Get("planTemplateFilter"),
            FileName = $"WISK wisk-plan-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            InitialDirectory = templateDirectory,
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var items = _draftItems.Select(item => new PlanTemplateItem(item.TaskId,
                item.Configuration?.Parameters.TryGetValue(item.TaskId, out var value) == true ? value : null)).ToImmutableArray();
            var template = new PlanTemplateDocument(PlanTemplateJson.SchemaVersion, "template-" + Guid.NewGuid().ToString("N"),
                Path.GetFileNameWithoutExtension(dialog.FileName), DateTimeOffset.UtcNow, items, _draftPolicy, false, false);
            await WriteTextAtomicallyAsync(Path.GetFullPath(dialog.FileName), PlanTemplateJson.Serialize(template, _catalog));
            ExecutionText.Text = Localization.Format("planTemplateExported", dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProfileValidationException)
        {
            ExecutionText.Text = Localization.Format("planTemplateExportFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private async void ImportPlanTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;
        var templateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "templates");
        Directory.CreateDirectory(templateDirectory);
        var dialog = new OpenFileDialog { Title = Localization.Get("importPlanTemplate"), Filter = Localization.Get("planTemplateFilter"), InitialDirectory = templateDirectory, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var template = PlanTemplateJson.Deserialize(await File.ReadAllTextAsync(Path.GetFullPath(dialog.FileName)), _catalog);
            if (_draftItems.Count > 0 && MessageBox.Show(Localization.Get("replacePendingPlan"), Localization.Get("importPlanTemplate"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var staged = new List<DraftPlanItem>();
            foreach (var item in template.Items)
            {
                var task = _catalog.Find(item.TaskId) ?? throw new ProfileValidationException($"Unknown task: {item.TaskId}");
                if (!task.IsAvailable) throw new ProfileValidationException(task.UnavailableReason ?? $"Task '{item.TaskId}' is unavailable.");
                var definition = TaskConfigurationCatalog.Get(item.TaskId);
                if (!definition.AllowsMultipleEntries && staged.Any(existing => existing.TaskId.Equals(item.TaskId, StringComparison.OrdinalIgnoreCase)))
                    throw new ProfileValidationException($"Task '{item.TaskId}' cannot be added more than once.");
                InteractiveConfiguration? configuration = item.Value is null ? null : new InteractiveConfiguration(
                    ImmutableDictionary<string, string>.Empty.Add(item.TaskId, item.Value), null);
                if (ConfigurationDialog.RequiresInput(item.TaskId) && configuration is null && !TryConfigureTask(item.TaskId, null, out configuration)) return;
                if (HasDuplicateAccounts(staged, configuration)) throw new ProfileValidationException(Localization.Get("duplicateAccount"));
                staged.Add(new DraftPlanItem(Guid.NewGuid().ToString("N"), item.TaskId, configuration));
            }

            _draftItems.Clear();
            _draftItems.AddRange(staged);
            _draftPolicy = template.Policy;
            AllowElevatedCheck.IsChecked = false;
            AllowHighRiskCheck.IsChecked = false;
            RefreshTaskPlanStates();
            SyncExecutionQueueWithDraft();
            ShowExecutionView(false);
            ExecutionText.Text = Localization.Format("planTemplateImported", staged.Count, template.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProfileValidationException)
        {
            ExecutionText.Text = Localization.Format("planTemplateImportFailed", SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private static bool HasDuplicateAccounts(IEnumerable<DraftPlanItem> staged, InteractiveConfiguration? configuration)
    {
        if (configuration?.Accounts is not { IsDefaultOrEmpty: false } accounts) return false;
        var existing = staged.SelectMany(item => item.Configuration?.Accounts ?? []).Select(account => account.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return accounts.Any(account => existing.Contains(account.Name));
    }

    private static async Task WriteTextAtomicallyAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new IOException("Output directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async void ExportRegistryRollback_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Localization.Get("exportRegistryRollback"),
            Filter = Localization.Get("registryFileFilter"),
            FileName = $"Wisk-registry-rollback-{DateTime.Now:yyyyMMdd-HHmmss}.reg"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state");
            var selectedRunId = _showCompletedView && CompletedRunsGrid.SelectedItem is CompletedRunRow selected ? selected.RunId : null;
            var count = selectedRunId is null
                ? await RegistryBackupExporter.ExportLatestAsync(stateRoot, dialog.FileName)
                : await RegistryBackupExporter.ExportRunAsync(stateRoot, selectedRunId, dialog.FileName);
            ExecutionText.Text = string.Format(Localization.Get("registryRollbackExported"), count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ExecutionText.Text = Localization.Get("registryRollbackUnavailable") + " " + SensitiveDataRedactor.Redact(exception.Message);
        }
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (SystemParameters.HighContrast) return;
        ApplyTheme(!_darkTheme);
    }

    private void ApplyTheme(bool darkTheme)
    {
        if (SystemParameters.HighContrast) return;
        _darkTheme = darkTheme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.First(dictionary => dictionary.Source?.OriginalString.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase) == true);
        current.Source = new Uri(_darkTheme ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        AppTitleBar.Foreground = Brushes.Black;
        AppTitleBar.Background = _darkTheme
            ? new SolidColorBrush(Color.FromRgb(231, 237, 245))
            : (Brush)FindResource("AppWindowBrush");
        var selectedIndex = WorkspaceTabs.SelectedIndex;
        WorkspaceTabs.SelectedIndex = -1;
        WorkspaceTabs.SelectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
        ThemeButton.Content = Localization.Get(_darkTheme ? "lightTheme" : "darkTheme");
        UpdateActiveNavigation();
    }

    private void PrepareExecution(ImmutablePlan plan, string summary)
    {
        if (!ExecutionRowsRepresent(plan)) RebuildExecutionRows(plan);
        foreach (var row in _executionRows)
            row.Update(TaskState.Pending, string.Empty, string.Empty, false, false);
        RunSummaryText.Text = summary;
        QueueCountText.Text = string.Format(Localization.Get("queueCount"), _executionRows.Count);
        SetExecutionPaneExpanded(true);
        ShowExecutionView(false);
    }

    private bool ExecutionRowsRepresent(ImmutablePlan plan)
    {
        var planned = plan.Tasks.Select(task => task.TaskId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _executionRows.Count > 0 &&
               _executionRows.All(row => planned.Contains(row.TaskId)) &&
               planned.All(taskId => _executionRows.Any(row => row.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase)));
    }

    private void RebuildExecutionRows(ImmutablePlan plan)
    {
        _executionRows.Clear();
        for (var index = 0; index < plan.Tasks.Length; index++)
        {
            var task = plan.Tasks[index];
            var summary = string.IsNullOrWhiteSpace(task.ParameterSummary) ? BuildDraftSummary(task.TaskId) : task.ParameterSummary;
            _executionRows.Add(new ExecutionRow(index + 1, task.TaskId, task.TaskId,
                _catalog.Find(task.TaskId)?.DisplayName ?? task.TaskId, summary));
        }
    }

    private void UpdateExecutionProgress(ExecutionProgress progress)
    {
        foreach (var row in _executionRows.Where(item => item.TaskId.Equals(progress.TaskId, StringComparison.OrdinalIgnoreCase)))
            row.Update(progress.State, string.Empty, SensitiveDataRedactor.Redact(progress.Message), false, false);
        RunSummaryText.Text = Localization.Format("runProgress", progress.RunId, progress.Completed, progress.Total, progress.TaskId, LocalizedState(progress.State));
        ExecutionText.Text = Localization.Format("taskProgress", progress.Completed, progress.Total, progress.TaskId, LocalizedState(progress.State));
        QueueCountText.Text = string.Format(Localization.Get("queueCount"), _executionRows.Count);
    }

    private void DisplayRunSnapshot(ImmutablePlan plan, RunStateSnapshot snapshot)
    {
        if (!ExecutionRowsRepresent(plan)) RebuildExecutionRows(plan);
        foreach (var row in _executionRows)
        {
            var result = snapshot.Results.FirstOrDefault(item => item.TaskId.Equals(row.TaskId, StringComparison.OrdinalIgnoreCase));
            if (result is not null)
                row.Update(result.State, result.Code, SensitiveDataRedactor.Redact(result.Message), result.Changed, result.RebootRequired);
        }
        RunSummaryText.Text = Localization.Format("runSnapshot", snapshot.RunId, LocalizedState(snapshot.State), snapshot.UpdatedAt) +
                              (snapshot.RecoveryRequired ? " | " + Localization.Get("stateRecoveryRequired") : string.Empty);
        QueueCountText.Text = string.Format(Localization.Get("queueCount"), _executionRows.Count);
        SetExecutionPaneExpanded(true);
    }

    private static string RunResultText(RunStateSnapshot snapshot) => Localization.Format(
        "runResult", snapshot.RunId, LocalizedState(snapshot.State),
        snapshot.Results.Count(result => result.State == TaskState.Succeeded),
        snapshot.Results.Count(result => result.State == TaskState.Failed));

    private static string LocalizedState(TaskState state) => Localization.Get("state" + state);

    private static string LocalizedVerification(VerificationStatus status) => Localization.Get(status switch
    {
        VerificationStatus.NotRequired => "verificationNotRequired",
        VerificationStatus.PendingRestart => "verificationPendingRestart",
        VerificationStatus.Verified => "verificationVerified",
        VerificationStatus.Failed => "verificationFailed",
        _ => "verificationUnknown"
    });

    private ProfileDocument? BuildInteractiveProfile(IReadOnlyCollection<string> ids)
    {
        var profile = new ProfileDocument("3.0", "interactive", ids.ToImmutableArray(), new ProfileTarget(), _draftPolicy,
            AllowElevatedCheck.IsChecked == true, AllowHighRiskCheck.IsChecked == true);
        var parameters = ImmutableDictionary<string, string>.Empty;
        var accounts = ImmutableArray.CreateBuilder<ProfileAccount>();
        foreach (var draft in _draftItems.Where(item => ConfigurationDialog.RequiresInput(item.TaskId)))
        {
            if (draft.Configuration is not { } configuration) return null;
            foreach (var pair in configuration.Parameters) parameters = parameters.SetItem(pair.Key, pair.Value);
            if (configuration.Accounts is { IsDefaultOrEmpty: false } configuredAccounts) accounts.AddRange(configuredAccounts);
        }
        return profile with
        {
            Parameters = parameters.IsEmpty ? null : parameters,
            Accounts = accounts.Count == 0 ? null : accounts.ToImmutable()
        };
    }

    private enum WorkspaceArea { Home, System, Registry, Software, Recovery }

    private sealed record LocalizedCategory(string Key, string DisplayName, bool IsSelected)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record DraftPlanItem(string InstanceId, string TaskId, InteractiveConfiguration? Configuration);

    private sealed class RecoveryRunRow(RegistryBackupRunSummary run)
    {
        public string RunId => run.RunId;
        public string UpdatedAt => run.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        public int RecordCount => run.RecordCount;
    }

    private sealed class RecoveryRecordRow(RegistryBackupRecord record, Catalog catalog)
    {
        public string TaskName => CatalogLocalization.TaskName(record.TaskId, catalog.Find(record.TaskId)?.DisplayName ?? record.TaskId);
        public string Location => $"{record.Hive}\\{record.Path}\\{record.ValueName}";
        public string RestoreExpression => record.RestoreExpression;
    }

    private sealed class TaskRow(TaskDescriptor task, Catalog catalog) : INotifyPropertyChanged
    {
        private int _planCount;
        private bool _planLocked;
        private SoftwareInventoryStatus? _softwareInventoryStatus;
        private bool _softwareInventoryPending;
        public string Id => task.Id;
        public string DisplayName => CatalogLocalization.TaskName(task.Id, task.DisplayName);
        public string Domain => CatalogLocalization.Domain(task.Domain);
        public string RawDomain => task.Domain;
        public string PackageId => task.PackageId ?? string.Empty;
        public string SoftwareAction => _softwareInventoryStatus == SoftwareInventoryStatus.UpgradeAvailable ? "upgrade" : "install";
        public string SettingMode => Localization.Get(task.Kind == TaskKind.RegistrySetting ? "fixedPreset" : Configuration.ConfigureBeforeAdd ? "configurableFeature" : "systemAction");
        public bool MatchesSoftwareFilter(string filter) => filter switch
        {
            "available" => task.IsAvailable,
            "installed" => _softwareInventoryStatus is SoftwareInventoryStatus.Installed or SoftwareInventoryStatus.UpgradeAvailable,
            "updates" => _softwareInventoryStatus == SoftwareInventoryStatus.UpgradeAvailable,
            _ => true
        };
        public TaskKind Kind => task.Kind;
        public RiskLevel RiskLevel => task.Risk;
        public string Risk => CatalogLocalization.Risk(task.Risk);
        public string Source => CatalogLocalization.Source(task.Source);
        public string Administrator => Localization.Get(task.RequiresAdministrator ? "yes" : "no");
        public string PermissionLabel => Localization.Get(task.RequiresAdministrator ? "administratorRequired" : "standardUser");
        public string Restart => Localization.Get(task.RequiresReboot ? "yes" : "no");
        public string Availability => task.IsAvailable
            ? Localization.Get(task.RequiresReboot ? "availableRestart" : "available")
             : task.UnavailableReason is null ? Localization.Get("unavailable") : Localization.Translate(task.UnavailableReason);
        public string SoftwareStatus => task.Kind != TaskKind.Winget
            ? Availability
            : Localization.Get(_softwareInventoryPending ? "softwareStatusDetecting" : _softwareInventoryStatus switch
            {
                SoftwareInventoryStatus.NotInstalled => "softwareStatusNotInstalled",
                SoftwareInventoryStatus.Installed => "softwareStatusInstalled",
                SoftwareInventoryStatus.UpgradeAvailable => "softwareStatusUpgradeAvailable",
                SoftwareInventoryStatus.DetectionFailed => "softwareStatusDetectionFailed",
                _ => "softwareStatusNotChecked"
            });
        public string CurrentState => task.Kind == TaskKind.Winget ? SoftwareStatus : Availability;
        public string RelationSummary
        {
            get
            {
                var relations = task.Relations ?? ImmutableArray<TaskRelation>.Empty;
                var visible = relations.Where(relation => relation.Kind is TaskRelationKind.Requires or TaskRelationKind.Recommends).ToArray();
                if (visible.Length == 0) return string.Empty;
                return string.Join(" · ", visible.Select(relation => Localization.Format(
                    relation.Kind == TaskRelationKind.Requires ? "requiresTask" : "recommendsTask",
                    CatalogLocalization.TaskName(relation.TargetTaskId, catalog.Find(relation.TargetTaskId)?.DisplayName ?? relation.TargetTaskId))));
            }
        }
        public bool IsAvailable => task.IsAvailable;
        private ImmutableArray<RegistryOptimizationEntry> RegistryEntries => task.Kind == TaskKind.RegistrySetting ? RegistryOptimizationCatalog.GetTaskEntries(task.Id) : [];
        public string Location => RegistryOptimizationCatalog.FindGroup(task.Id)?.Location ?? (RegistryEntries.IsDefaultOrEmpty ? string.Empty : RegistryEntries[0].Location);
        public string TargetValue => RegistryEntries.IsDefaultOrEmpty ? string.Empty : RegistryEntries.Length > 1
            ? string.Format(Localization.Get("registryValueCount"), RegistryEntries.Length)
            : RegistryEntries[0].Operation == RegistryOperationKind.DeleteValue ? Localization.Get("deleteValue") : RegistryEntries[0].RawValue;
        public string Scope => RegistryOptimizationCatalog.FindGroup(task.Id)?.Scope ?? (RegistryEntries.IsDefaultOrEmpty ? string.Empty : RegistryEntries[0].Hive);
        private TaskConfigurationDefinition Configuration => TaskConfigurationCatalog.Get(Id);
        public string AddLabel => Localization.Get(_planCount > 0 && !Configuration.AllowsMultipleEntries
            ? (Configuration.ConfigureBeforeAdd ? "configured" : "added")
            : task.Kind == TaskKind.Winget ? (_softwareInventoryStatus == SoftwareInventoryStatus.Installed ? "softwareStatusInstalled" : SoftwareAction == "upgrade" ? "upgradeApp" : "installApp")
            : (Configuration.ConfigureBeforeAdd ? "configureAndAdd" : "add"));
        public bool CanAdd => !_planLocked && IsAvailable && !(task.Kind == TaskKind.Winget && _softwareInventoryStatus == SoftwareInventoryStatus.Installed) && (_planCount == 0 || Configuration.AllowsMultipleEntries);
        public void SetPlanCount(int value)
        {
            if (_planCount == value) return;
            _planCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
        public void SetPlanLocked(bool value)
        {
            if (_planLocked == value) return;
            _planLocked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAdd)));
        }
        public void SetSoftwareInventoryPending()
        {
            _softwareInventoryPending = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SoftwareStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentState)));
        }
        public void SetSoftwareInventory(SoftwareInventoryStatus status)
        {
            _softwareInventoryPending = false;
            _softwareInventoryStatus = status;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAdd)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SoftwareStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentState)));
        }
        public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class CompletedRunRow(RunStateSnapshot snapshot, ExecutionHistoryVerification verification) : INotifyPropertyChanged
    {
        public RunStateSnapshot Snapshot => snapshot;
        public string RunId => snapshot.RunId;
        public string Title => snapshot.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        public string Summary => Localization.Format("completedRunSummary", snapshot.Results.Length, ShortRunId(snapshot.RunId)) +
            (verification.Status == VerificationStatus.NotRequired ? string.Empty : $" · {LocalizedVerification(verification.Status)}");
        public string StateDisplay => LocalizedState(DisplayState);
        public VerificationStatus VerificationStatus => verification.Status;
        private TaskState DisplayState => snapshot.State == TaskState.NeedsReboot ? TaskState.Succeeded : snapshot.State;
        public string StatusGlyph => DisplayState switch
        {
            _ when verification.Status == VerificationStatus.Failed => "×",
            _ when verification.Status is VerificationStatus.PendingRestart or VerificationStatus.Unknown => "!",
            TaskState.Succeeded or TaskState.NeedsReboot => "✓",
            TaskState.Failed => "×",
            TaskState.Cancelled => "!",
            _ => "•"
        };
        public Brush StatusBrush => DisplayState switch
        {
            _ when verification.Status == VerificationStatus.Failed => Brushes.IndianRed,
            _ when verification.Status is VerificationStatus.PendingRestart or VerificationStatus.Unknown => Brushes.DarkOrange,
            TaskState.Succeeded or TaskState.NeedsReboot => Brushes.SeaGreen,
            TaskState.Failed or TaskState.Cancelled => Brushes.IndianRed,
            _ => Brushes.DarkOrange
        };
        public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        public event PropertyChangedEventHandler? PropertyChanged;
        public ExecutionResult ResultFor(ExecutionResult result)
        {
            var normalized = result.State == TaskState.NeedsReboot ? result with { State = TaskState.Succeeded } : result;
            return verification.TaskStatuses.TryGetValue(result.TaskId, out var status)
                ? normalized with { VerificationStatus = status }
                : normalized;
        }
        private static string ShortRunId(string value) => value.Length <= 8 ? value : value[..8];
    }

    private sealed class CompletedTaskRow(ExecutionResult result, Catalog catalog) : INotifyPropertyChanged
    {
        public string TaskId => result.TaskId;
        public string DisplayName => CatalogLocalization.TaskName(result.TaskId, catalog.Find(result.TaskId)?.DisplayName ?? result.TaskId);
        public string StateDisplay => LocalizedState(result.State);
        public string Code => result.Code;
        public bool CanRetry => result.State == TaskState.Failed && !result.TaskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase);
        public string Details => $"{DisplayName}\n{Localization.Get("state")}: {StateDisplay} · {Localization.Get("code")}: {result.Code}" +
            $"\n{Localization.Get("failureStage")}: {result.FailureStage ?? Localization.Get("notAvailable")}" +
            $" · {Localization.Get("processExitCode")}: {(result.ProcessExitCode?.ToString(CultureInfo.InvariantCulture) ?? Localization.Get("notAvailable"))}" +
            $"\n{Localization.Get("verificationStatus")}: {LocalizedVerification(result.VerificationStatus)} · {Localization.Get("rebootRequired")}: {Localization.Get(result.RebootRequired ? "yes" : "no")}" +
            $"\n{Localization.Get("message")}: {SensitiveDataRedactor.Redact(result.Message)}" +
            $"\n{Localization.Get("suggestedAction")}: {FailureAdvice(result.Code)}" +
            $"\n{result.StartedAt?.ToLocalTime():g} - {result.CompletedAt?.ToLocalTime():g}";
        public string StatusGlyph => result.VerificationStatus switch
        {
            VerificationStatus.Failed => "×",
            VerificationStatus.PendingRestart or VerificationStatus.Unknown => "!",
            _ => result.State switch { TaskState.Succeeded or TaskState.NeedsReboot => "✓", TaskState.Failed => "×", TaskState.Cancelled => "!", TaskState.Skipped => "–", _ => "•" }
        };
        public Brush StatusBrush => result.VerificationStatus switch
        {
            VerificationStatus.Failed => Brushes.IndianRed,
            VerificationStatus.PendingRestart or VerificationStatus.Unknown => Brushes.DarkOrange,
            _ => result.State switch { TaskState.Succeeded or TaskState.NeedsReboot => Brushes.SeaGreen, TaskState.Failed or TaskState.Cancelled => Brushes.IndianRed, _ => Brushes.DarkOrange }
        };
        public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        public event PropertyChangedEventHandler? PropertyChanged;
        private static string FailureAdvice(string code) => Enum.TryParse<ErrorCode>(code, true, out var parsed) ? parsed switch
        {
            ErrorCode.NotAdministrator or ErrorCode.AccessDenied => Localization.Get("adviceAdministrator"),
            ErrorCode.MissingPowerShell or ErrorCode.MissingWinGet => Localization.Get("adviceMissingTool"),
            ErrorCode.NetworkUnavailable => Localization.Get("adviceNetwork"),
            ErrorCode.Timeout => Localization.Get("adviceTimeout"),
            ErrorCode.VerificationFailed => Localization.Get("adviceVerification"),
            _ => Localization.Get("adviceReviewDiagnostics")
        } : Localization.Get("adviceReviewDiagnostics");
    }

    private sealed class ExecutionRow(int order, string instanceId, string taskId, string displayName, string configurationSummary) : INotifyPropertyChanged
    {
        private int _order = order;
        private TaskState _state = TaskState.Pending;
        private string _code = string.Empty;
        private string _message = string.Empty;
        private bool _changed;
        private bool _restart;
        private string _configurationSummary = configurationSummary;

        public int Order => _order;
        public string InstanceId => instanceId;
        public string TaskId => taskId;
        public bool CanConfigure => ConfigurationDialog.RequiresInput(taskId);
        public string EditLabel => Localization.Get("edit");
        public string RemoveLabel => Localization.Get("remove");
        public string DisplayName => CatalogLocalization.TaskName(taskId, displayName);
        public TaskState State => _state;
        public string StateDisplay => CatalogLocalization.State(_state);
        public string Code => _code;
        public string Message => _message;
        public string ConfigurationSummary => _configurationSummary;
        public string Changed => Localization.Get(_changed ? "yes" : "no");
        public string Restart => Localization.Get(_restart ? "yes" : "no");
        public string StatusGlyph => _state switch
        {
            TaskState.Succeeded or TaskState.NeedsReboot => "✓",
            TaskState.Failed => "×",
            TaskState.Cancelled => "!",
            TaskState.Checking or TaskState.Ready or TaskState.Running => "•",
            TaskState.Skipped => "–",
            TaskState.NeedsManualReview or TaskState.UnsupportedPrerequisite or TaskState.UnavailableExtension or TaskState.RecoveryRequired => "!",
            _ => "○"
        };
        public Brush StatusBrush => _state switch
        {
            TaskState.Succeeded or TaskState.NeedsReboot => Brushes.SeaGreen,
            TaskState.Failed or TaskState.Cancelled => Brushes.IndianRed,
            TaskState.Checking or TaskState.Ready or TaskState.Running => Brushes.DodgerBlue,
            TaskState.Skipped => Brushes.DarkGoldenrod,
            TaskState.NeedsManualReview or TaskState.UnsupportedPrerequisite or TaskState.UnavailableExtension or TaskState.RecoveryRequired => Brushes.DarkOrange,
            _ => Brushes.SlateGray
        };

        public void SetOrder(int value)
        {
            if (_order == value) return;
            _order = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Order)));
        }

        public void SetConfigurationSummary(string value)
        {
            if (_configurationSummary == value) return;
            _configurationSummary = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConfigurationSummary)));
        }

        public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        public void Update(TaskState state, string code, string message, bool changed, bool restart)
        {
            _state = state;
            _code = code;
            _message = message.Length > 1024 ? message[..1024] : message;
            _changed = changed;
            _restart = restart;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
