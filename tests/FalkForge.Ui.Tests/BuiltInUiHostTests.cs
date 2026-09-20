namespace FalkForge.Ui.Tests;

using System.IO;
using System.Security.Cryptography;
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
    private const string ManifestSha256 =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void ResolveArgs_WithManifestPipeAndSecret_ReturnsAllValues()
    {
        var result = BuiltInUiHost.ResolveArgs(
            ["--manifest", @"C:\cache\installer.manifest.json", "--manifest-sha256", ManifestSha256,
             "--pipe", "FalkForge_abc", "--secret-pipe", "falkforge_init_xyz"]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(@"C:\cache\installer.manifest.json", result.Value.ManifestPath);
        Assert.Equal(ManifestSha256, result.Value.ManifestSha256);
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
    public void ResolveArgs_ConnectedWithoutManifestDigest_FailsLoud()
    {
        var result = BuiltInUiHost.ResolveArgs(
            ["--manifest", "installer.manifest.json", "--pipe", "FalkForge_abc",
             "--secret-pipe", "falkforge_init_xyz"]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("--manifest-sha256", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadManifest_WhenFileDoesNotMatchEngineDigest_FailsIntegrityCheck()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, "attacker manifest"u8.ToArray());
            var expected = Convert.ToHexString(SHA256.HashData("publisher manifest"u8));

            var result = BuiltInUiHost.LoadManifest(path, expected);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        }
        finally
        {
            File.Delete(path);
        }
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
