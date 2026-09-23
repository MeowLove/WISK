using System.Xml.Linq;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class UiInteractionContractTests
{
    [Fact]
    public void ExecutionPaneExposesPreflightAndNoChangeSimulation()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));
        var dialog = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "PreflightDialog.xaml"));

        Assert.Contains("Click=\"Preflight_Click\"", xaml);
        Assert.Contains("Click=\"SimulatePlan_Click\"", xaml);
        Assert.Contains("BuildPreflightReport(plan, compatibility)", code);
        Assert.Contains("new SimulationTaskExecutor()", code);
        Assert.Contains("x:Name=\"ChecksGrid\"", dialog);
    }

    [Fact]
    public void RecoveryWorkspaceExposesRunSpecificEvidenceWithoutAutomaticImport()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"RecoveryTab\"", xaml);
        Assert.Contains("x:Name=\"RecoveryRunsGrid\"", xaml);
        Assert.Contains("x:Name=\"RecoveryRecordsGrid\"", xaml);
        Assert.Contains("Click=\"ExportRecoveryRollback_Click\"", xaml);
        Assert.Contains("RegistryBackupCatalogReader.LoadRunAsync", code);
        Assert.Contains("OpenSystemTarget(\"rstrui.exe\")", code);
        Assert.DoesNotContain("reg.exe import", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CategoryNavigatorsUseWrappingPanelsWithoutHorizontalScrollers()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Wisk.App", "MainWindow.xaml");
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[] { "SystemCategoryTabs", "RegistryCategoryTabs", "SoftwareCategoryTabs" })
        {
            var control = document.Descendants(presentation + "ItemsControl")
                .Single(element => (string?)element.Attribute(x + "Name") == name);
            Assert.NotEmpty(control.Descendants(presentation + "WrapPanel"));
            Assert.Empty(control.Ancestors(presentation + "ScrollViewer"));
        }
    }

    [Fact]
    public void AddFlowUsesDraftInstancesAndDoesNotConfigureDuringApply()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));
        var definitions = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "TaskConfigurationCatalog.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));

        Assert.Contains("private readonly List<DraftPlanItem> _draftItems", source);
        Assert.Contains("private void AddTask_Click", source);
        Assert.Contains("new DraftPlanItem(Guid.NewGuid()", source);
        Assert.DoesNotContain("TaskRow_PropertyChanged", source);
        Assert.Contains("[\"accounts-local\"] = new(TaskConfigurationMode.Collection, true, AllowsMultipleEntries: true)", definitions);
        Assert.Contains("Click=\"AddTask_Click\"", xaml);
        Assert.Contains("Tag=\"{Binding InstanceId}\"", xaml);
    }

    [Fact]
    public void ConfigurableAddAndEditOnlyCommitAfterConfigurationIsAccepted()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Wisk.App", "MainWindow.xaml.cs"));
        var addStart = source.IndexOf("private void AddTask_Click", StringComparison.Ordinal);
        var addEnd = source.IndexOf("private bool TryConfigureTask", addStart, StringComparison.Ordinal);
        var editStart = source.IndexOf("private void EditQueueItem_Click", addEnd, StringComparison.Ordinal);
        var editEnd = source.IndexOf("private bool ValidateDraftConfiguration", editStart, StringComparison.Ordinal);
        Assert.True(addStart >= 0 && addEnd > addStart && editStart > addEnd && editEnd > editStart);

        var add = source[addStart..addEnd];
        var configure = add.IndexOf("!TryConfigureTask(item.Id, null, out configuration)) return;", StringComparison.Ordinal);
        var commitAdditions = add.IndexOf("_draftItems.AddRange(additions);", StringComparison.Ordinal);
        Assert.True(configure >= 0 && commitAdditions > configure);

        var edit = source[editStart..editEnd];
        var configureExisting = edit.IndexOf("TryConfigureTask(item.TaskId, item.Configuration, out var configuration)", StringComparison.Ordinal);
        var commitEdit = edit.IndexOf("_draftItems[index] = item with { Configuration = configuration };", StringComparison.Ordinal);
        Assert.True(configureExisting >= 0 && commitEdit > configureExisting);
        Assert.Contains("if (!TryConfigureTask(item.TaskId, item.Configuration, out var configuration)) return;", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationRequestsAdministratorBeforeCreatingTheMainWindow()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Wisk.App", "app.manifest");
        var document = XDocument.Load(path);
        var requestedLevel = document.Descendants().Single(element => element.Name.LocalName == "requestedExecutionLevel");

        Assert.Equal("requireAdministrator", (string?)requestedLevel.Attribute("level"));
        Assert.Equal("false", (string?)requestedLevel.Attribute("uiAccess"));
    }

    [Fact]
    public void ExecutionPaneSeparatesPendingAndCompletedBatches()
    {
        var root = RepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.NotNull(document.Descendants(presentation + "Grid").SingleOrDefault(element => (string?)element.Attribute(x + "Name") == "PendingExecutionView"));
        Assert.NotNull(document.Descendants(presentation + "Grid").SingleOrDefault(element => (string?)element.Attribute(x + "Name") == "CompletedExecutionView"));
        Assert.NotNull(document.Descendants(presentation + "DataGrid").SingleOrDefault(element => (string?)element.Attribute(x + "Name") == "CompletedRunsGrid"));
        Assert.Contains("private void SetPlanLocked(bool locked)", source);
        Assert.Contains("_draftItems.Clear();", source);
        Assert.Contains("await RefreshCompletedRunsAsync();", source);
        Assert.Contains("ShowExecutionView(true);", source);
    }

    [Fact]
    public void ApplyPromptsForMissingRiskAuthorizationBeforeBuildingThePlan()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Wisk.App", "MainWindow.xaml.cs"));
        var confirmationCall = source.IndexOf("if (!ConfirmRiskAuthorization(ids)) return;", StringComparison.Ordinal);
        var profileBuild = source.IndexOf("var profile = BuildInteractiveProfile(ids);", StringComparison.Ordinal);

        Assert.True(confirmationCall >= 0);
        Assert.True(profileBuild > confirmationCall);
        Assert.Contains("MessageBoxButton.YesNo", source);
        Assert.Contains("AllowElevatedCheck.IsChecked = true", source);
        Assert.Contains("AllowHighRiskCheck.IsChecked = true", source);
    }

    [Fact]
    public void HomeExposesReadOnlySelfTestAction()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));

        Assert.Contains("Content=\"{local:Loc selfTest}\"", xaml);
        Assert.Contains("Click=\"SelfTest_Click\"", xaml);
        Assert.Contains("SelfTestReportExporter.ExportBundleAsync", source);
        Assert.Contains("RunAsync(compatibility, progress: progress)", source);
    }

    [Fact]
    public void CompletedRunsExposeFailureDetailsAndSingleTaskRetry()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"CompletedTasksGrid\"", xaml);
        Assert.Contains("Click=\"RetryFailedTask_Click\"", xaml);
        Assert.Contains("x:Name=\"CompletedTaskDetailsText\"", xaml);
        Assert.Contains("SingleTaskRetryPlan.Create", source);
        Assert.Contains("ExecuteAsync(plan", source);
    }

    [Fact]
    public void CompletedRunsExposeConfirmedHistoryCleanup()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));

        Assert.Contains("Click=\"CleanupHistory_Click\"", xaml);
        Assert.Contains("CleanupAsync(policy, dryRun: true)", source);
        Assert.Contains("MessageBoxButton.YesNo", source);
        Assert.Contains("await store.CleanupAsync(policy)", source);
    }

    [Fact]
    public void PendingPlanSupportsSafeTemplateImportAndExport()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));

        Assert.Contains("Click=\"ImportPlanTemplate_Click\"", xaml);
        Assert.Contains("Click=\"ExportPlanTemplate_Click\"", xaml);
        Assert.Contains("PlanTemplateJson.Deserialize", source);
        Assert.Contains("PlanTemplateJson.Serialize", source);
        Assert.Contains("TryConfigureTask(item.TaskId", source);
        Assert.Contains("WriteTextAtomicallyAsync", source);
    }

    [Fact]
    public void ExecutionPaneShowsImpactAndExportsTheSelectedRunRollback()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"PlanImpactText\"", xaml);
        Assert.Contains("x:Name=\"RestorePointRecommendationText\"", xaml);
        Assert.Contains("x:Name=\"RestorePointRecommendationButton\"", xaml);
        Assert.Contains("Tag=\"safety-restore-point\"", xaml);
        Assert.Contains("Content=\"{local:Loc exportSelectedRollback}\"", xaml);
        Assert.Contains("PlanImpactAnalyzer.Analyze", source);
        Assert.Contains("RegistryBackupExporter.ExportRunAsync", source);
    }

    [Fact]
    public void SystemWorkspaceProvidesNativeManagementEntrypoints()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));

        Assert.Contains("Click=\"OpenWindowsUpdate_Click\"", xaml);
        Assert.Contains("Click=\"OpenPowerSettings_Click\"", xaml);
        Assert.Contains("ms-settings:windowsupdate", source);
        Assert.Contains("ms-settings:powersleep", source);
    }

    [Fact]
    public void UiQualityGateCoversThemesRtlAccessibilityAndProvenance()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "MainWindow.xaml.cs"));
        var localization = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "Localization.cs"));
        var project = File.ReadAllText(Path.Combine(root, "src", "Wisk.App", "Wisk.App.csproj"));
        var capture = File.ReadAllText(Path.Combine(root, "tools", "Capture-UiSmoke.ps1"));

        Assert.Contains("AutomationProperties.Name", xaml);
        Assert.Contains("x:Name=\"SidebarColumn\"", xaml);
        Assert.Contains("UpdateResponsiveLayout()", source);
        Assert.Contains("FlowDirection.RightToLeft", localization);
        Assert.Contains("Themes/Dark.xaml", source);
        Assert.Contains("AssemblyMetadata Include=\"SourceCommit\"", project);
        Assert.Contains("home-light-ar-rtl.png", capture);
        Assert.Contains("colors.Count -lt 8", capture);
    }

    [Fact]
    public void ImportedTemplatesDoNotCarryRiskAuthorizationIntoTheCurrentSession()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Wisk.App", "MainWindow.xaml.cs"));
        var exportStart = source.IndexOf("private async void ExportPlanTemplate_Click", StringComparison.Ordinal);
        var exportEnd = source.IndexOf("private async void ImportPlanTemplate_Click", exportStart, StringComparison.Ordinal);
        var start = source.IndexOf("private async void ImportPlanTemplate_Click", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool HasDuplicateAccounts", start, StringComparison.Ordinal);
        Assert.True(exportStart >= 0 && exportEnd > exportStart && start >= 0 && end > start);
        var exportHandler = source[exportStart..exportEnd];
        Assert.Contains("_draftPolicy, false, false)", exportHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowElevatedCheck.IsChecked", exportHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowHighRiskCheck.IsChecked", exportHandler, StringComparison.Ordinal);
        var importHandler = source[start..end];
        var replaceDraft = importHandler.IndexOf("_draftItems.Clear();", StringComparison.Ordinal);
        var deserialize = importHandler.IndexOf("PlanTemplateJson.Deserialize", StringComparison.Ordinal);
        var staged = importHandler.IndexOf("var staged =", StringComparison.Ordinal);

        Assert.True(replaceDraft >= 0);
        Assert.True(deserialize >= 0 && staged > deserialize && replaceDraft > staged);
        Assert.Contains("AllowElevatedCheck.IsChecked = false;", importHandler[replaceDraft..], StringComparison.Ordinal);
        Assert.Contains("AllowHighRiskCheck.IsChecked = false;", importHandler[replaceDraft..], StringComparison.Ordinal);
        Assert.DoesNotContain("AllowElevatedCheck.IsChecked = template.AllowElevated", importHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowHighRiskCheck.IsChecked = template.AllowHighRisk", importHandler, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, "src", "Wisk.App", "MainWindow.xaml");
            if (File.Exists(marker)) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("WISK repository root could not be located from the test output directory.");
    }
}
