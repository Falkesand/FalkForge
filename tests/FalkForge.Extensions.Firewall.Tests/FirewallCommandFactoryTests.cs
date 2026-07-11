using System.Text;
using Xunit;

namespace FalkForge.Extensions.Firewall.Tests;

/// <summary>
/// Command-generation tests for <see cref="FirewallCommandFactory"/>. The generated commands run in a
/// deferred, elevated (SYSTEM) custom action, so injection safety is a privilege-boundary concern:
/// a rule name or program path is untrusted input and must never be able to inject additional
/// PowerShell statements. Commands are emitted as <c>powershell.exe -EncodedCommand &lt;base64&gt;</c>;
/// the tests decode the base64 back to the script and assert on that.
/// </summary>
public sealed class FirewallCommandFactoryTests
{
    private static FirewallRuleModel Rule(
        string id = "Web",
        string name = "Web Server",
        string? port = "8080",
        FirewallProtocol protocol = FirewallProtocol.Tcp,
        FirewallDirection direction = FirewallDirection.Inbound,
        string? program = null,
        FirewallRuleAction action = FirewallRuleAction.Allow)
        => new()
        {
            Id = id,
            Name = name,
            Port = port,
            Protocol = protocol,
            Direction = direction,
            Program = program,
            Action = action,
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
    public void BasicRule_ProducesCreateInstallAndRemoveRollbackUninstall()
    {
        var steps = FirewallCommandFactory.BuildSteps([Rule()]);

        var step = Assert.Single(steps);
        Assert.Equal("Fw_Web", step.Id);

        var install = DecodeScript(step.InstallCommand);
        Assert.Contains("New-NetFirewallRule", install, StringComparison.Ordinal);
        Assert.Contains("-Name 'Fw_Web'", install, StringComparison.Ordinal);
        Assert.Contains("-DisplayName 'Web Server'", install, StringComparison.Ordinal);
        Assert.Contains("-Direction Inbound", install, StringComparison.Ordinal);
        Assert.Contains("-Protocol TCP", install, StringComparison.Ordinal);
        Assert.Contains("-LocalPort '8080'", install, StringComparison.Ordinal);

        // Rollback and uninstall both remove exactly the rule install created (by -Name).
        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);
        Assert.Contains("Remove-NetFirewallRule -Name 'Fw_Web'", DecodeScript(step.RollbackCommand!), StringComparison.Ordinal);
        Assert.Contains("Remove-NetFirewallRule -Name 'Fw_Web'", DecodeScript(step.UninstallCommand!), StringComparison.Ordinal);
    }

    [Fact]
    public void EncodedCommand_TransportCarriesNoQuoteOrShellMetacharacters()
    {
        // The whole point of -EncodedCommand: the emitted Target has no quote, bracket or space beyond
        // the fixed prefix, so nothing a crafted rule value contains can break out of the command line
        // or trigger MSI Formatted substitution. The base64 payload is [A-Za-z0-9+/=] only.
        var steps = FirewallCommandFactory.BuildSteps([Rule(name: "a'\"; calc; [X]")]);
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
    public void SingleQuoteInName_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        // A classic PowerShell injection attempt in the display name.
        var malicious = "a'; Start-Process calc.exe; '";
        var steps = FirewallCommandFactory.BuildSteps([Rule(name: malicious)]);

        var install = DecodeScript(steps[0].InstallCommand);

        // The value is wrapped in a single-quoted literal with every ' doubled → inert.
        Assert.Contains("-DisplayName 'a''; Start-Process calc.exe; '''", install, StringComparison.Ordinal);

        // There must be no place where the literal terminates early and hands control back to the parser.
        Assert.DoesNotContain("-DisplayName 'a';", install, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramPathWithQuote_IsQuotedSafely()
    {
        var steps = FirewallCommandFactory.BuildSteps(
            [Rule(program: @"C:\Program' Files\app.exe")]);

        var install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("-Program 'C:\\Program'' Files\\app.exe'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleIdWithIllegalChars_IsSanitizedToValidIdentifier()
    {
        var steps = FirewallCommandFactory.BuildSteps([Rule(id: "web rule-1!")]);
        Assert.Equal("Fw_web_rule_1_", steps[0].Id);
    }

    [Fact]
    public void InterpreterIsFullyQualified_NotABarePowershellExe()
    {
        // Bare "powershell.exe" would be resolved relative to the action's working directory
        // (TARGETDIR) before PATH → a planted powershell.exe could run as SYSTEM. The absolute
        // [SystemFolder] path closes that binary-planting vector.
        var install = FirewallCommandFactory.BuildSteps([Rule()])[0].InstallCommand;
        Assert.StartsWith("[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe", install, StringComparison.Ordinal);
        Assert.DoesNotContain("\"powershell.exe", install, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleWithoutCondition_LeavesInstallConditionNullForEmitterDefault()
    {
        var step = FirewallCommandFactory.BuildSteps([Rule()])[0];
        Assert.Null(step.InstallCondition);
    }

    [Fact]
    public void RuleCondition_IsThreadedIntoInstallCondition()
    {
        // A rule condition must actually gate the create action now that rules execute; before this
        // seam the condition only reached inert table data and was silently dropped at runtime.
        var rule = Rule();
        rule = new FirewallRuleModel
        {
            Id = rule.Id, Name = rule.Name, Port = rule.Port, Protocol = rule.Protocol,
            Direction = rule.Direction, Action = rule.Action, Condition = "MYPROP=\"1\"",
        };

        var step = FirewallCommandFactory.BuildSteps([rule])[0];
        Assert.NotNull(step.InstallCondition);
        Assert.Contains("MYPROP=\"1\"", step.InstallCondition!, StringComparison.Ordinal);
        Assert.Contains("NOT Installed", step.InstallCondition!, StringComparison.Ordinal);
    }

    [Fact]
    public void OutboundBlockRule_MapsDirectionAndAction()
    {
        var steps = FirewallCommandFactory.BuildSteps(
        [
            Rule(direction: FirewallDirection.Outbound, action: FirewallRuleAction.Block),
        ]);

        var install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("-Direction Outbound", install, StringComparison.Ordinal);
        Assert.Contains("-Action Block", install, StringComparison.Ordinal);
    }
}
