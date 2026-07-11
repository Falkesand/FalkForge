using System.Text;
using FalkForge.Extensions.Util.InternetShortcut;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.InternetShortcut;

/// <summary>
/// Command-generation tests for <see cref="InternetShortcutCommandFactory"/>. The generated script
/// writes a <c>.url</c> file (an INI file) via a deferred, elevated action, since the native
/// <c>IniFile</c>/<c>WriteIniValues</c> mechanism is unreachable from an extension (see the factory's
/// type remarks). The target directory is passed as a LIVE double-quoted trailing argument outside the
/// base64 payload so an MSI Formatted token like <c>[INSTALLDIR]</c> resolves at install time.
/// </summary>
public sealed class InternetShortcutCommandFactoryTests
{
    private static InternetShortcutModel Model(
        string id = "Home",
        string name = "App Home",
        string target = "https://example.com",
        string directory = @"C:\ProgramData\App",
        string? iconFile = null,
        int iconIndex = 0)
        => new()
        {
            Id = id,
            Name = name,
            Target = target,
            Directory = directory,
            IconFile = iconFile,
            IconIndex = iconIndex,
        };

    // Splits a command into its base64-decoded script and its live trailing argument (the directory).
    private static (string Script, string TrailingArg) Decode(string command)
    {
        const string marker = "-EncodedCommand ";
        int idx = command.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"command is not an -EncodedCommand invocation: {command}");
        int argStart = command.IndexOf(" \"", idx, StringComparison.Ordinal);
        Assert.True(argStart >= 0, $"command has no trailing directory argument: {command}");
        string base64 = command[(idx + marker.Length)..argStart].Trim();
        string trailing = command[(argStart + 2)..].TrimEnd('"');
        return (Encoding.Unicode.GetString(Convert.FromBase64String(base64)), trailing);
    }

    [Fact]
    public void BasicShortcut_ProducesCreateInstallAndRemoveRollbackUninstall()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model()]);

        var step = Assert.Single(steps);
        Assert.Equal("Isc_Home", step.Id);

        var (install, dir) = Decode(step.InstallCommand);
        Assert.Equal(@"C:\ProgramData\App", dir);
        Assert.Contains("$dir = $args[0]", install, StringComparison.Ordinal);
        Assert.Contains("Join-Path -Path $dir -ChildPath 'App Home.url'", install, StringComparison.Ordinal);
        Assert.Contains("'[InternetShortcut]'", install, StringComparison.Ordinal);
        Assert.Contains("'URL=https://example.com'", install, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $path -Value $lines -Encoding Default", install, StringComparison.Ordinal);

        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);
        Assert.Contains("Remove-Item -LiteralPath $path -Force", Decode(step.RollbackCommand!).Script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $path -Force", Decode(step.UninstallCommand!).Script, StringComparison.Ordinal);
        // Rollback and uninstall carry the same directory trailing argument so they target the same file.
        Assert.Equal(@"C:\ProgramData\App", Decode(step.RollbackCommand!).TrailingArg);
        Assert.Equal(@"C:\ProgramData\App", Decode(step.UninstallCommand!).TrailingArg);
    }

    [Fact]
    public void DirectoryToken_SurvivesLiveInTheTrailingArgument()
    {
        // Regression guard: [INSTALLDIR] must reach the Target as literal bracket text (outside base64)
        // so the installer resolves it — not be buried inside the encoded script.
        var steps = InternetShortcutCommandFactory.BuildSteps([Model(directory: "[INSTALLDIR]")]);
        Assert.EndsWith(" \"[INSTALLDIR]\"", steps[0].InstallCommand, StringComparison.Ordinal);
        Assert.Equal("[INSTALLDIR]", Decode(steps[0].InstallCommand).TrailingArg);
    }

    [Fact]
    public void IconFile_IncludesIconFileAndIconIndexLines()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps(
            [Model(iconFile: @"C:\Icons\app.ico", iconIndex: 2)]);

        string install = Decode(steps[0].InstallCommand).Script;
        Assert.Contains(@"'IconFile=C:\Icons\app.ico'", install, StringComparison.Ordinal);
        Assert.Contains("'IconIndex=2'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutIconFile_OmitsIconLines()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model()]);
        string install = Decode(steps[0].InstallCommand).Script;
        Assert.DoesNotContain("IconFile=", install, StringComparison.Ordinal);
        Assert.DoesNotContain("IconIndex=", install, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatesDirectoryIfMissing()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model()]);
        string install = Decode(steps[0].InstallCommand).Script;
        Assert.Contains("New-Item -ItemType Directory", install, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodedScript_CarriesNoQuoteOrShellMetacharactersInTheBase64Segment()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model(target: "https://example.com/a'\"; calc; [X] & B")]);
        string command = steps[0].InstallCommand;

        const string marker = "-EncodedCommand ";
        int start = command.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = command.IndexOf(" \"", start, StringComparison.Ordinal);
        string payload = command[start..end];
        Assert.Matches("^[A-Za-z0-9+/=]+$", payload);
    }

    [Fact]
    public void SingleQuoteInTargetUrl_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        var malicious = "https://example.com/'; Start-Process calc.exe; '";
        var steps = InternetShortcutCommandFactory.BuildSteps([Model(target: malicious)]);

        string install = Decode(steps[0].InstallCommand).Script;
        Assert.Contains("''; Start-Process calc.exe; '''", install, StringComparison.Ordinal);
    }

    [Fact]
    public void IdWithIllegalChars_IsSanitizedToValidIdentifier()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model(id: "home link!")]);
        Assert.Equal("Isc_home_link_", steps[0].Id);
    }

    [Fact]
    public void InterpreterIsFullyQualified_NotABarePowershellExe()
    {
        var install = InternetShortcutCommandFactory.BuildSteps([Model()])[0].InstallCommand;
        Assert.StartsWith("[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe", install, StringComparison.Ordinal);
    }
}
