using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Extensions.Firewall.Tests;

/// <summary>
/// Tests for <see cref="FirewallRules"/>'s FWL003 port-format guard directly (mirrors the
/// extension-level FWL001 test in <see cref="FirewallExtensionTests"/>).
/// </summary>
public sealed class FirewallRulesTests
{
    private static bool HasFwl003Violation(string port)
    {
        var extension = new FirewallExtension();
        extension.AddRule(r => r.Id("FW1").Name("HTTP").Port(port));

        var rules = extension.GetValidationRules();
        var engine = new ValidationEngine(new RuleRegistry(rules));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        var report = engine.Run(package);
        return report.Violations.Any(v => v.RuleId.Value == "FWL003");
    }

    [Fact]
    public void Build_ValidPort_NoFwl003Violation()
    {
        Assert.False(HasFwl003Violation("8080"));
    }

    [Fact]
    public void Build_TrailingNewlinePort_HasFwl003Violation()
    {
        // .NET regex `$` matches end-of-string OR immediately before a single trailing '\n',
        // even without RegexOptions.Multiline, so "8080\n" would slip through an
        // otherwise-correct ^...$ anchor. \A and \z are absolute string boundaries with no
        // such exception.
        Assert.True(HasFwl003Violation("8080" + "\n"));
    }

    [Fact]
    public void Build_ArabicIndicDigitPort_HasFwl003ViolationInsteadOfThrowing()
    {
        // \d in .NET is Unicode-aware and matches non-ASCII decimal digits (e.g. Arabic-Indic
        // U+0660-U+0669). Before the [0-9] fix, PortFormatRegex.IsMatch("٨٠٨٠")
        // succeeded and the value flowed straight into int.Parse, which throws FormatException for
        // these code points -- crashing the validation rule instead of reporting a normal FWL003
        // violation.
        Assert.True(HasFwl003Violation("٨٠٨٠"));
    }
}
