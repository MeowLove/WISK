using WindowsInitializer.Contracts;
using WindowsInitializer.Core;
using WindowsInitializer.Execution;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class SelfTestRunnerTests
{
    [Fact]
    public async Task SelfTestRunsCheckOnlyAndClassifiesResults()
    {
        var executor = new RecordingExecutor();
        var compatibility = new CompatibilitySnapshot("Windows 11", "25H2", 26200, 1, "Enterprise", "X64", "X64",
            true, true, true, true, true, true, false, true, true);

        var report = await new SelfTestRunner(new Catalog(), executor).RunAsync(compatibility, ["runtime-dotnet-8"]);

        var result = Assert.Single(report.Results);
        Assert.Equal(SelfTestDisposition.Ready, result.Disposition);
        Assert.Equal(1, executor.CheckCount);
        Assert.Equal(0, executor.ApplyCount);
        Assert.Equal(0, executor.VerifyCount);
    }

    [Fact]
    public async Task SelfTestReportsUnknownTasksWithoutCallingExecutor()
    {
        var executor = new RecordingExecutor();
        var compatibility = new CompatibilitySnapshot("Windows 11", "26200", "X64", true, true, true);

        var report = await new SelfTestRunner(new Catalog(), executor).RunAsync(compatibility, ["missing-task"]);

        var result = Assert.Single(report.Results);
        Assert.Equal(SelfTestDisposition.Failed, result.Disposition);
        Assert.Equal(ErrorCode.UnknownTask, result.Code);
        Assert.Equal(0, executor.CheckCount);
    }

    [Fact]
    public async Task SelfTestReportExportsJsonAndHtml()
    {
        var root = Path.Combine(Path.GetTempPath(), "windows-initializer-self-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var compatibility = new CompatibilitySnapshot("Windows 11", "26200", "X64", true, true, true);
            var report = await new SelfTestRunner(new Catalog(), new RecordingExecutor()).RunAsync(compatibility, ["runtime-dotnet-8"]);

            var paths = await SelfTestReportExporter.ExportBundleAsync(report, root, "report");

            Assert.Contains("\"taskId\": \"runtime-dotnet-8\"", await File.ReadAllTextAsync(paths.JsonPath));
            Assert.Contains("<table>", await File.ReadAllTextAsync(paths.HtmlPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class RecordingExecutor : IInitializerTaskExecutor
    {
        public int CheckCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int VerifyCount { get; private set; }

        public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
        {
            CheckCount++;
            return Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.Ready, ErrorCode.None, "Ready"));
        }

        public Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context)
        {
            ApplyCount++;
            throw new InvalidOperationException("Self-test must not apply tasks.");
        }

        public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
        {
            VerifyCount++;
            throw new InvalidOperationException("Self-test must not verify tasks.");
        }
    }
}
