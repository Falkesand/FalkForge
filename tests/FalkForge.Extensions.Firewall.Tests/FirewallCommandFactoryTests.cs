using Xunit;

namespace FalkForge.Extensions.Firewall.Tests;

/// <summary>
/// Command-generation tests for <see cref="FirewallCommandFactory"/>. The generated commands run in a
/// deferred, elevated (SYSTEM) custom action, so injection safety is a privilege-boundary concern:
/// a rule name or program path is untrusted input and must never be able to inject additional
/// PowerShell statements or MSI property substitutions.
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

    [Fact]
    public void BasicRule_ProducesCreateInstallAndRemoveRollbackUninstall()
    {
        var steps = FirewallCommandFactory.BuildSteps([Rule()]);

        var step = Assert.Single(steps);
        Assert.Equal("Fw_Web", step.Id);
        Assert.Contains("New-NetFirewallRule", step.InstallCommand, StringComparison.Ordinal);
        Assert.Contains("-Name 'Fw_Web'", step.InstallCommand, StringComparison.Ordinal);
        Assert.Contains("-DisplayName 'Web Server'", step.InstallCommand, StringComparison.Ordinal);
        Assert.Contains("-Direction Inbound", step.InstallCommand, StringComparison.Ordinal);
        Assert.Contains("-Protocol TCP", step.InstallCommand, StringComparison.Ordinal);
        Assert.Contains("-LocalPort '8080'", step.InstallCommand, StringComparison.Ordinal);

        // Rollback and uninstall both remove exactly the rule install created (by -Name).
        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);
        Assert.Contains("Remove-NetFirewallRule -Name 'Fw_Web'", step.RollbackCommand!, StringComparison.Ordinal);
        Assert.Contains("Remove-NetFirewallRule -Name 'Fw_Web'", step.UninstallCommand!, StringComparison.Ordinal);
    }

    [Fact]
    public void BasicRule_FitsWithinMsiTargetLimit()
    {
        // The install command must fit the CHAR(255) CustomAction.Target column for a typical rule,
        // otherwise the mechanism's length guard would reject even ordinary firewall rules.
        var steps = FirewallCommandFactory.BuildSteps([Rule()]);
        Assert.True(steps[0].InstallCommand.Length <= 255,
            $"Install command is {steps[0].InstallCommand.Length} chars: {steps[0].InstallCommand}");
    }

    [Fact]
    public void SingleQuoteInName_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        // A classic PowerShell injection attempt in the display name.
        var malicious = "a'; Start-Process calc.exe; '";
        var steps = FirewallCommandFactory.BuildSteps([Rule(name: malicious)]);

        var install = steps[0].InstallCommand;

        // The value is wrapped in a single-quoted literal with every ' doubled → inert.
        Assert.Contains("-DisplayName 'a''; Start-Process calc.exe; '''", install, StringComparison.Ordinal);

        // There must be no place where the literal terminates early and hands control back to the
        // parser: the only unescaped single quote pairs are the ones we intentionally emit.
        Assert.DoesNotContain("-DisplayName 'a';", install, StringComparison.Ordinal);
    }

    [Fact]
    public void MsiFormatMetacharactersInValue_AreEscaped()
    {
        // '[' / ']' would otherwise be interpreted as an MSI Formatted property/environment
        // substitution in the CustomAction.Target field.
        var steps = FirewallCommandFactory.BuildSteps([Rule(name: "svc[INSTALLDIR]")]);

        var install = steps[0].InstallCommand;
        Assert.DoesNotContain("[INSTALLDIR]", install, StringComparison.Ordinal);
        Assert.Contains("[\\[]INSTALLDIR[\\]]", install, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramPathWithQuote_IsQuotedSafely()
    {
        var steps = FirewallCommandFactory.BuildSteps(
            [Rule(program: @"C:\Program' Files\app.exe")]);

        var install = steps[0].InstallCommand;
        Assert.Contains("-Program 'C:\\Program'' Files\\app.exe'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleIdWithIllegalChars_IsSanitizedToValidIdentifier()
    {
        var steps = FirewallCommandFactory.BuildSteps([Rule(id: "web rule-1!")]);
        Assert.Equal("Fw_web_rule_1_", steps[0].Id);
    }

    [Fact]
    public void OutboundBlockRule_MapsDirectionAndAction()
    {
        var steps = FirewallCommandFactory.BuildSteps(
        [
            Rule(direction: FirewallDirection.Outbound, action: FirewallRuleAction.Block),
        ]);

        Assert.Contains("-Direction Outbound", steps[0].InstallCommand, StringComparison.Ordinal);
        Assert.Contains("-Action Block", steps[0].InstallCommand, StringComparison.Ordinal);
    }
}
