namespace FalkForge.Ui.Tests;

using System.IO;
using System.Text.RegularExpressions;
using FalkForge.Ui;
using Xunit;

/// <summary>
/// Regression guard: the built-in UI host's launch task must never be discarded.
/// <para>
/// <c>App.OnStartup</c> used to read <c>_ = BuiltInUiHost.LaunchAsync(...)</c>. When
/// <c>LaunchAsync</c> threw — which it did on every run, because ReactiveUI 23.x was never
/// initialized — nothing observed the fault. No message box, no log line, nothing on stderr, and
/// a zero exit code. WPF kept pumping a message loop with no window, so the installer looked
/// like it had hung rather than failed. That is what hid the ReactiveUI defect for months.
/// </para>
/// </summary>
public sealed class BuiltInUiLaunchFaultTests
{
    // Matches a discard of a call whose member name ends in LaunchAsync, e.g.
    // `_ = BuiltInUiHost.LaunchAsync(` or `_ = LaunchAsync(`. Deliberately narrow: it does not
    // flag `_ = BuiltInUiHost.RunAndReportFailureAsync(BuiltInUiHost.LaunchAsync(...), ...)`,
    // which is the supported way to start the host, because there the discarded call is the
    // observer and the launch task is its argument.
    private static readonly Regex DiscardedLaunchPattern = new(
        @"(?<![\w.])_\s*=\s*(?:\w+\s*\.\s*)*LaunchAsync\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public async Task RunAndReportFailureAsync_WhenTheLaunchFaults_HandsTheExceptionToTheReporter()
    {
        var thrown = new InvalidOperationException("ReactiveUI has not been initialized.");
        Exception? reported = null;

        await BuiltInUiHost.RunAndReportFailureAsync(Task.FromException(thrown), ex => reported = ex);

        Assert.Same(thrown, reported);
    }

    [Fact]
    public async Task RunAndReportFailureAsync_WhenTheLaunchSucceeds_ReportsNothing()
    {
        var reports = 0;

        await BuiltInUiHost.RunAndReportFailureAsync(Task.CompletedTask, _ => reports++);

        Assert.Equal(0, reports);
    }

    [Fact]
    public void NoProductionSource_DiscardsTheBuiltInUiLaunchTask()
    {
        var srcDir = FindSrcDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (DiscardedLaunchPattern.IsMatch(text))
                offenders.Add(file);
        }

        Assert.True(offenders.Count == 0,
            "Found a discarded built-in UI launch task in: " + string.Join(", ", offenders) +
            ". A discarded Task swallows the startup fault, so the installer shows no window, "
            + "reports nothing, and still exits 0. Pass the launch task to "
            + "BuiltInUiHost.RunAndReportFailureAsync so a fault reaches the user and the exit code.");
    }

    /// <summary>
    /// Positive control for the scan above: the pattern really does match the shape this test
    /// forbids. Without this, a typo in the regex would turn the guard into a test that cannot
    /// fail.
    /// </summary>
    [Fact]
    public void ThePattern_MatchesTheShapeItForbids_AndNotTheSupportedCall()
    {
        Assert.Matches(DiscardedLaunchPattern, "        _ = BuiltInUiHost.LaunchAsync(this, args, manifest);");
        Assert.Matches(DiscardedLaunchPattern, "_ = LaunchAsync(app);");
        Assert.DoesNotMatch(DiscardedLaunchPattern,
            "_ = BuiltInUiHost.RunAndReportFailureAsync(BuiltInUiHost.LaunchAsync(this, args, manifest), report);");
        Assert.DoesNotMatch(DiscardedLaunchPattern, "_disposables = new CompositeDisposable();");
    }

    /// <summary>
    /// Walks up from the running test assembly to the repo root (marked by <c>FalkForge.slnx</c>)
    /// and returns the <c>src</c> directory, so the scan works wherever the repo is cloned.
    /// </summary>
    private static string FindSrcDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FalkForge.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src");
    }
}
