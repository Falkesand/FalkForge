using System.Text;
using FalkForge.Extensions.Util.InternetShortcut;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.InternetShortcut;

/// <summary>
/// Command-generation tests for <see cref="InternetShortcutCommandFactory"/>. The generated script
/// writes a <c>.url</c> file (an INI file) via a deferred, elevated action, since the native
/// <c>IniFile</c>/<c>WriteIniValues</c> mechanism is unreachable from an extension (see the factory's
/// type remarks).
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

    private static string DecodeScript(string command)
    {
        const string marker = "-EncodedCommand ";
        int idx = command.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"command is not an -EncodedCommand invocation: {command}");
        string base64 = command[(idx + marker.Length)..].Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    [Fact]
    public void BasicShortcut_ProducesCreateInstallAndRemoveRollbackUninstall()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model()]);

        var step = Assert.Single(steps);
        Assert.Equal("Isc_Home", step.Id);

        string install = DecodeScript(step.InstallCommand);
        Assert.Contains(@"Join-Path -Path 'C:\ProgramData\App' -ChildPath 'App Home.url'", install, StringComparison.Ordinal);
        Assert.Contains("'[InternetShortcut]'", install, StringComparison.Ordinal);
        Assert.Contains("'URL=https://example.com'", install, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $path -Value $lines -Encoding ASCII", install, StringComparison.Ordinal);

        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);
        Assert.Contains("Remove-Item -LiteralPath $path -Force", DecodeScript(step.RollbackCommand!), StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $path -Force", DecodeScript(step.UninstallCommand!), StringComparison.Ordinal);
    }

    [Fact]
    public void IconFile_IncludesIconFileAndIconIndexLines()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps(
            [Model(iconFile: @"C:\Icons\app.ico", iconIndex: 2)]);

        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains(@"'IconFile=C:\Icons\app.ico'", install, StringComparison.Ordinal);
        Assert.Contains("'IconIndex=2'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutIconFile_OmitsIconLines()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model()]);
        string install = DecodeScript(steps[0].InstallCommand);
        Assert.DoesNotContain("IconFile=", install, StringComparison.Ordinal);
        Assert.DoesNotContain("IconIndex=", install, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatesDirectoryIfMissing()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model()]);
        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("New-Item -ItemType Directory", install, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodedCommand_TransportCarriesNoQuoteOrShellMetacharacters()
    {
        var steps = InternetShortcutCommandFactory.BuildSteps([Model(target: "https://example.com/a'\"; calc; [X] & B")]);
        string command = steps[0].InstallCommand;

        const string marker = "-EncodedCommand ";
        string payload = command[(command.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        Assert.DoesNotContain('"', payload);
        Assert.DoesNotContain('\'', payload);
        Assert.DoesNotContain('[', payload);
        Assert.DoesNotContain(';', payload);
        Assert.Matches("^[A-Za-z0-9+/=]+$", payload);
    }

    [Fact]
    public void SingleQuoteInTargetUrl_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        var malicious = "https://example.com/'; Start-Process calc.exe; '";
        var steps = InternetShortcutCommandFactory.BuildSteps([Model(target: malicious)]);

        string install = DecodeScript(steps[0].InstallCommand);
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
