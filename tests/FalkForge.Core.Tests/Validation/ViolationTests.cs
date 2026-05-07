using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class ViolationTests
{
    [Fact]
    public void Violation_exposes_all_constructor_fields()
    {
        var id = new RuleId("PKG001");
        var path = ModelPath.Root.Field("Name");
        var violation = new Violation(id, Severity.Error, path, "Name is required.");

        Assert.Equal("PKG001", violation.RuleId.Value);
        Assert.Equal(Severity.Error, violation.Severity);
        Assert.Equal("Name", violation.Path.ToString());
        Assert.Equal("Name is required.", violation.Message);
    }

    [Fact]
    public void Violation_supports_structural_equality()
    {
        var id = new RuleId("SVC001");
        var path = ModelPath.Root.Field("Services").Index(0);
        var a = new Violation(id, Severity.Error, path, "msg");
        var b = new Violation(id, Severity.Error, path, "msg");

        Assert.Equal(a, b);
    }
}
