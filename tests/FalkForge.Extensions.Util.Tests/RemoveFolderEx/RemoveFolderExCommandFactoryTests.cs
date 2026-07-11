using System.Text;
using FalkForge.Extensions.Util.RemoveFolderEx;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.RemoveFolderEx;

/// <summary>
/// Command-generation tests for <see cref="RemoveFolderExCommandFactory"/>: the live-token
/// (CustomActionData) install path, the literal-directory install/uninstall path, and the runtime
/// path-safety guard that refuses to delete a root path regardless of source.
/// </summary>
public sealed class RemoveFolderExCommandFactoryTests
{
    private static RemoveFolderExModel Model(
        string id = "Cache",
        string? directory = null,
        string? property = null,
        RemoveFolderExInstallMode mode = RemoveFolderExInstallMode.Uninstall)
        => new()
        {
            Id = id,
            Directory = directory,
            Property = property,
            InstallMode = mode,
        };

    private static string DecodeScript(string command)
    {
        const string marker = "-EncodedCommand ";
        int idx = command.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"command is not an -EncodedCommand invocation: {command}");
        int end = command.IndexOf(" \"", idx, StringComparison.Ordinal);
        string base64 = (end >= 0 ? command[(idx + marker.Length)..end] : command[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    [Fact]
    public void LiteralDirectory_OnUninstall_BakesLiteralIntoUninstallScript_NoCustomActionData()
    {
        var steps = RemoveFolderExCommandFactory.BuildSteps(
            [Model(directory: @"C:\ProgramData\App\Cache", mode: RemoveFolderExInstallMode.Uninstall)]);

        var step = Assert.Single(steps);
        Assert.Equal("Rfx_Cache", step.Id);
        Assert.Null(step.CustomActionData);
        Assert.Equal("0", step.InstallCondition); // install action is a gated no-op
        Assert.NotNull(step.UninstallCommand);

        string uninstall = DecodeScript(step.UninstallCommand!);
        Assert.Contains(@"$p = 'C:\ProgramData\App\Cache'", uninstall, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $full -Recurse -Force", uninstall, StringComparison.Ordinal);
    }

    [Fact]
    public void LiteralDirectory_OnBoth_ProducesInstallAndUninstallCommands()
    {
        var steps = RemoveFolderExCommandFactory.BuildSteps(
            [Model(directory: @"C:\Data\Temp", mode: RemoveFolderExInstallMode.Both)]);

        var step = Assert.Single(steps);
        Assert.NotEqual("0", step.InstallCondition);
        Assert.Equal(@"C:\Data\Temp", step.CustomActionData);
        Assert.NotNull(step.UninstallCommand);

        string install = DecodeScript(step.InstallCommand);
        Assert.Contains("$p = $args[0]", install, StringComparison.Ordinal);
        Assert.EndsWith("\"[CustomActionData]\"", step.InstallCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void PropertyReference_OnInstall_UsesLiveTokenAsCustomActionData()
    {
        var steps = RemoveFolderExCommandFactory.BuildSteps(
            [Model(property: "LOGFOLDER", mode: RemoveFolderExInstallMode.Install)]);

        var step = Assert.Single(steps);
        Assert.Equal("[LOGFOLDER]", step.CustomActionData);
        Assert.Null(step.UninstallCommand);
        Assert.EndsWith("\"[CustomActionData]\"", step.InstallCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void LiteralDirectoryWithBrackets_IsMsiFormatEscapedInCustomActionData()
    {
        var steps = RemoveFolderExCommandFactory.BuildSteps(
            [Model(directory: @"C:\Data\[weird]", mode: RemoveFolderExInstallMode.Install)]);

        // MsiFormatEscape neutralizes stray brackets so they cannot be mistaken for a live token.
        Assert.Equal(@"C:\Data\[\[]weird[\]]", steps[0].CustomActionData);
    }

    [Fact]
    public void LiteralDirectoryWithSingleQuote_OnUninstall_IsDoubledInBakedLiteral()
    {
        var steps = RemoveFolderExCommandFactory.BuildSteps(
            [Model(directory: @"C:\Data\App's Cache", mode: RemoveFolderExInstallMode.Uninstall)]);

        string uninstall = DecodeScript(steps[0].UninstallCommand!);
        Assert.Contains(@"$p = 'C:\Data\App''s Cache'", uninstall, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardedScript_RefusesRootPathAtRuntime()
    {
        var steps = RemoveFolderExCommandFactory.BuildSteps(
            [Model(property: "INSTALLFOLDER", mode: RemoveFolderExInstallMode.Install)]);

        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("GetFullPath", install, StringComparison.Ordinal);
        Assert.Contains("GetPathRoot", install, StringComparison.Ordinal);
        Assert.Contains("refusing to remove root or unsafe path", install, StringComparison.Ordinal);
    }

    [Fact]
    public void IdWithIllegalChars_IsSanitizedToValidIdentifier()
    {
        var steps = RemoveFolderExCommandFactory.BuildSteps(
            [Model(id: "cache dir!", directory: @"C:\Data", mode: RemoveFolderExInstallMode.Uninstall)]);
        Assert.Equal("Rfx_cache_dir_", steps[0].Id);
    }

    [Fact]
    public void InterpreterIsFullyQualified_NotABarePowershellExe()
    {
        var install = RemoveFolderExCommandFactory.BuildSteps(
            [Model(directory: @"C:\Data", mode: RemoveFolderExInstallMode.Both)])[0].InstallCommand;
        Assert.StartsWith("[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe", install, StringComparison.Ordinal);
    }
}
