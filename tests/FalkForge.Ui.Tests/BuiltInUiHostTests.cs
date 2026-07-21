namespace FalkForge.Ui.Tests;

using FalkForge;
using FalkForge.Ui;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Tests.ViewModels;
using Xunit;

/// <summary>
/// Covers the turnkey <c>FalkForge.Ui.exe</c> built-in host entry logic (#56): the engine
/// spawns this process with <c>--manifest</c> / <c>--pipe</c> / <c>--secret-pipe</c> and expects
/// it to render the built-in wizard. Before the fix the WPF entry never read those args nor
/// created a window, so a spawned host showed nothing.
/// </summary>
public sealed class BuiltInUiHostTests
{
    [Fact]
    public void ResolveArgs_WithManifestPipeAndSecret_ReturnsAllValues()
    {
        var result = BuiltInUiHost.ResolveArgs(
            ["--manifest", @"C:\cache\installer.manifest.json", "--pipe", "FalkForge_abc", "--secret-pipe", "falkforge_init_xyz"]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(@"C:\cache\installer.manifest.json", result.Value.ManifestPath);
        Assert.Equal("FalkForge_abc", result.Value.PipeName);
        Assert.Equal("falkforge_init_xyz", result.Value.SecretPipeName);
    }

    [Fact]
    public void ResolveArgs_ManifestOnly_SucceedsWithNullPipe_ForDesignPreview()
    {
        var result = BuiltInUiHost.ResolveArgs(["--manifest", "installer.manifest.json"]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("installer.manifest.json", result.Value.ManifestPath);
        Assert.Null(result.Value.PipeName);
        Assert.Null(result.Value.SecretPipeName);
    }

    [Fact]
    public void ResolveArgs_MissingManifest_FailsLoud()
    {
        // A built-in host spawned without a manifest is a misuse: it must fail loud, not
        // silently show a blank window.
        var result = BuiltInUiHost.ResolveArgs(["--pipe", "FalkForge_abc"]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("--manifest", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveArgs_BlankManifestValue_FailsLoud()
    {
        var result = BuiltInUiHost.ResolveArgs(["--manifest", "   "]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void ResolveArgs_EmptyArgs_FailsLoud()
    {
        var result = BuiltInUiHost.ResolveArgs([]);

        Assert.True(result.IsFailure);
    }

    [WpfFact]
    public void BuildWindow_BindsDefaultShell_WithWelcomeAsFirstPage()
    {
        var engine = new TestInstallerEngine();

        var window = BuiltInUiHost.BuildWindow(engine);

        var shell = Assert.IsType<DefaultShellViewModel>(window.DataContext);
        Assert.Same(engine, shell.Engine);
        Assert.Equal(7, shell.Pages.Count);
        Assert.IsType<WelcomePageViewModel>(shell.CurrentPage);
    }
}
