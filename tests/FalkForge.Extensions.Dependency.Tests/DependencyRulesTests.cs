using System.Linq;
using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class DependencyRulesTests
{
    private static readonly PackageModel EmptyPackage = new()
    {
        Name = "TestApp",
        Manufacturer = "TestCo",
        Version = new Version(1, 0, 0),
    };

    private static IEnumerable<Violation> Evaluate(DependencyExtension extension, string ruleId)
    {
        var rule = extension.GetValidationRules().Single(r => r.Id.Value == ruleId);
        return rule.Evaluate(RuleContext.ForTest(EmptyPackage));
    }

    [Fact]
    public void DEP008_Flags_UnparseableMinVersion()
    {
        var extension = new DependencyExtension();
        extension.Requires("Acme.Foo", c => c.ConsumerKey("Acme.App").MinVersion("not-a-version"));

        var violations = Evaluate(extension, "DEP008").ToList();

        var violation = Assert.Single(violations);
        Assert.Contains("MinVersion", violation.Message, StringComparison.Ordinal);
        Assert.Contains("not-a-version", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DEP008_Flags_UnparseableMaxVersion()
    {
        var extension = new DependencyExtension();
        extension.Requires("Acme.Foo", c => c.ConsumerKey("Acme.App").MaxVersion("v2-beta"));

        var violations = Evaluate(extension, "DEP008").ToList();

        var violation = Assert.Single(violations);
        Assert.Contains("MaxVersion", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DEP008_Flags_BothBounds_WhenBothUnparseable()
    {
        var extension = new DependencyExtension();
        extension.Requires("Acme.Foo", c => c.ConsumerKey("Acme.App").MinVersion("x").MaxVersion("y"));

        var violations = Evaluate(extension, "DEP008").ToList();

        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void DEP008_DoesNotFlag_ValidBounds()
    {
        var extension = new DependencyExtension();
        extension.Requires("Acme.Foo", c => c.ConsumerKey("Acme.App").MinVersion("1.0.0.0").MaxVersion("2.0.0.0"));

        var violations = Evaluate(extension, "DEP008").ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void DEP008_DoesNotFlag_PresenceOnlyConsumer()
    {
        var extension = new DependencyExtension();
        extension.Requires("Acme.Foo", c => c.ConsumerKey("Acme.App"));

        var violations = Evaluate(extension, "DEP008").ToList();

        Assert.Empty(violations);
    }
}
