using System.Text;
using FalkForge.Extensions.Util.QuietExec;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.QuietExec;

/// <summary>
/// Command-generation tests for <see cref="QuietExecCommandFactory"/>. Commands run in a deferred,
/// elevated (SYSTEM) custom action; the author's command line is trusted (it is their own install
/// step), but the transport itself must not be corruptible by anything the command line legitimately
/// contains (quotes, brackets, ampersands, ...).
/// </summary>
public sealed class QuietExecCommandFactoryTests
{
    private static QuietExecModel Model(
        string id = "Provision",
        string commandLine = "setup.exe /quiet",
        string? workingDirectory = null,
        string? condition = null,
        string? rollbackCommandLine = null)
        => new()
        {
            Id = id,
            CommandLine = commandLine,
            WorkingDirectory = workingDirectory,
            Condition = condition,
            RollbackCommandLine = rollbackCommandLine,
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
    public void BasicCommand_ProducesEncodedInstallCommand()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model()]);

        var step = Assert.Single(steps);
        Assert.Equal("Qe_Provision", step.Id);

        string install = DecodeScript(step.InstallCommand);
        Assert.Contains("$Env:ComSpec /c 'setup.exe /quiet'", install, StringComparison.Ordinal);
        Assert.Contains("exit $LASTEXITCODE", install, StringComparison.Ordinal);
        Assert.Null(step.RollbackCommand);
        Assert.Null(step.UninstallCommand);
    }

    [Fact]
    public void WorkingDirectory_EmitsSetLocationBeforeCommand()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(workingDirectory: @"C:\Program Files\App")]);

        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains(@"Set-Location -LiteralPath 'C:\Program Files\App'", install, StringComparison.Ordinal);
        Assert.True(
            install.IndexOf("Set-Location", StringComparison.Ordinal) <
            install.IndexOf("$Env:ComSpec", StringComparison.Ordinal),
            "Set-Location must run before the command");
    }

    [Fact]
    public void RollbackCommandLine_ProducesEncodedRollbackCommand()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(rollbackCommandLine: "undo.exe /quiet")]);

        Assert.NotNull(steps[0].RollbackCommand);
        string rollback = DecodeScript(steps[0].RollbackCommand!);
        Assert.Contains("$Env:ComSpec /c 'undo.exe /quiet'", rollback, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodedCommand_TransportCarriesNoQuoteOrShellMetacharacters()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(commandLine: "a'\"; calc; [X] & B")]);
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
    public void SingleQuoteInCommandLine_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        var malicious = "a'; Start-Process calc.exe; '";
        var steps = QuietExecCommandFactory.BuildSteps([Model(commandLine: malicious)]);

        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("'a''; Start-Process calc.exe; '''", install, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandIdWithIllegalChars_IsSanitizedToValidIdentifier()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(id: "provision step-1!")]);
        Assert.Equal("Qe_provision_step_1_", steps[0].Id);
    }

    [Fact]
    public void InterpreterIsFullyQualified_NotABarePowershellExe()
    {
        var install = QuietExecCommandFactory.BuildSteps([Model()])[0].InstallCommand;
        Assert.StartsWith("[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe", install, StringComparison.Ordinal);
    }

    [Fact]
    public void Condition_IsThreadedIntoInstallCondition()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(condition: "MYPROP=\"1\"")]);

        Assert.NotNull(steps[0].InstallCondition);
        Assert.Contains("MYPROP=\"1\"", steps[0].InstallCondition!, StringComparison.Ordinal);
        Assert.Contains("NOT Installed", steps[0].InstallCondition!, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutCondition_LeavesInstallConditionNullForEmitterDefault()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model()]);
        Assert.Null(steps[0].InstallCondition);
    }
}
