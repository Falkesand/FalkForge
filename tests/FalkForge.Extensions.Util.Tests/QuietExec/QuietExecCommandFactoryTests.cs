using FalkForge.Extensions.Util.QuietExec;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.QuietExec;

/// <summary>
/// Command-generation tests for <see cref="QuietExecCommandFactory"/>. The author's command line runs
/// in a deferred, elevated (SYSTEM) custom action. Unlike the other Util factories it is emitted with
/// its MSI Formatted tokens LIVE (not base64-encoded), because the command is fully author-supplied and
/// must be able to reference install-time properties such as <c>[INSTALLDIR]</c> / <c>[ENVIRONMENT]</c>
/// — which the installer only resolves if they are visible in the CustomAction.Target (i.e. outside any
/// base64 blob).
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

    [Fact]
    public void BasicCommand_RunsViaFullyQualifiedCmd_WithNoBase64()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model()]);

        var step = Assert.Single(steps);
        Assert.Equal("Qe_Provision", step.Id);

        // The command is NOT base64-encoded; the author's command line is visible verbatim in the Target.
        Assert.Equal("[SystemFolder]cmd.exe /s /c setup.exe /quiet", step.InstallCommand);
        Assert.DoesNotContain("-EncodedCommand", step.InstallCommand, StringComparison.Ordinal);
        Assert.Null(step.RollbackCommand);
        Assert.Null(step.UninstallCommand);
    }

    [Fact]
    public void MsiFormattedTokens_SurviveLiveInTheTarget_SoTheInstallerCanResolveThem()
    {
        // This is the regression guard for the bug where base64-encoding buried [INSTALLDIR] so the
        // installer never substituted it. The tokens must appear as literal bracket text in the Target.
        var steps = QuietExecCommandFactory.BuildSteps(
            [Model(commandLine: "\"[INSTALLDIR]app.exe\" --env [ENVIRONMENT]", workingDirectory: "[INSTALLDIR]")]);

        string install = steps[0].InstallCommand;
        Assert.Contains("[INSTALLDIR]", install, StringComparison.Ordinal);
        Assert.Contains("[ENVIRONMENT]", install, StringComparison.Ordinal);
        Assert.Contains("cd /d \"[INSTALLDIR]\" &&", install, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkingDirectory_EmitsCdBeforeCommand()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(workingDirectory: @"C:\Program Files\App")]);

        string install = steps[0].InstallCommand;
        Assert.Contains(@"cd /d ""C:\Program Files\App"" && setup.exe /quiet", install, StringComparison.Ordinal);
    }

    [Fact]
    public void RollbackCommandLine_ProducesRollbackCommand()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(rollbackCommandLine: "undo.exe /quiet")]);

        Assert.NotNull(steps[0].RollbackCommand);
        Assert.Equal("[SystemFolder]cmd.exe /s /c undo.exe /quiet", steps[0].RollbackCommand);
    }

    [Fact]
    public void InterpreterIsFullyQualified_NotABareCmdExe()
    {
        // Bare "cmd.exe" would be resolved relative to the action's working directory (TARGETDIR) before
        // PATH → a planted cmd.exe could run as SYSTEM. The absolute [SystemFolder] path closes that.
        var install = QuietExecCommandFactory.BuildSteps([Model()])[0].InstallCommand;
        Assert.StartsWith("[SystemFolder]cmd.exe /s /c ", install, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandIdWithIllegalChars_IsSanitizedToValidIdentifier()
    {
        var steps = QuietExecCommandFactory.BuildSteps([Model(id: "provision step-1!")]);
        Assert.Equal("Qe_provision_step_1_", steps[0].Id);
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
