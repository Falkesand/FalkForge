using FalkForge;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class ConditionTests
{
    // ── Static pre-composed conditions ──

    [Theory]
    [InlineData(nameof(Condition.Is64BitOS), "VersionNT64 OR Msix64")]
    [InlineData(nameof(Condition.IsPrivileged), "Privileged")]
    [InlineData(nameof(Condition.IsAdmin), "AdminUser")]
    [InlineData(nameof(Condition.IsTerminalServer), "TerminalServer")]
    [InlineData(nameof(Condition.IsWindows10OrLater), "VersionNT >= 603")]
    [InlineData(nameof(Condition.IsWindows11OrLater), "WindowsBuildNumber >= 22000")]
    [InlineData(nameof(Condition.IsInstalled), "Installed")]
    [InlineData(nameof(Condition.IsInstalling), "NOT Installed")]
    [InlineData(nameof(Condition.IsUninstalling), "REMOVE=\"ALL\"")]
    [InlineData(nameof(Condition.IsRepairing), "REINSTALL")]
    public void PreComposedCondition_ReturnsExpectedExpression(string propertyName, string expected)
    {
        var prop = typeof(Condition).GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var condition = (Condition)prop.GetValue(null)!;
        Assert.Equal(expected, condition.ToString());
    }

    // ── Factory methods ──

    [Fact]
    public void Property_ReturnsPropertyNameAsTruthinessCheck()
    {
        var condition = Condition.Property("MY_PROP");

        Assert.Equal("MY_PROP", condition.ToString());
    }

    [Fact]
    public void Raw_ReturnsExactStringPassthrough()
    {
        var condition = Condition.Raw("VersionNT >= 601 AND AdminUser");

        Assert.Equal("VersionNT >= 601 AND AdminUser", condition.ToString());
    }

    // ── Logical operators ──

    [Fact]
    public void AndOperator_ParenthesizesBothOperands()
    {
        var result = Condition.Is64BitOS & Condition.IsPrivileged;

        Assert.Equal("(VersionNT64 OR Msix64) AND (Privileged)", result.ToString());
    }

    [Fact]
    public void OrOperator_ParenthesizesBothOperands()
    {
        var result = Condition.IsPrivileged | Condition.IsAdmin;

        Assert.Equal("(Privileged) OR (AdminUser)", result.ToString());
    }

    [Fact]
    public void NotOperator_ParenthesizesOperand()
    {
        var result = !Condition.IsInstalled;

        Assert.Equal("NOT (Installed)", result.ToString());
    }

    [Fact]
    public void ComplexNesting_CombinesAndOrNot()
    {
        var result = (Condition.Is64BitOS & Condition.IsPrivileged) | !Condition.IsInstalled;

        Assert.Equal(
            "((VersionNT64 OR Msix64) AND (Privileged)) OR (NOT (Installed))",
            result.ToString());
    }

    // ── Implicit string conversion ──

    [Fact]
    public void ImplicitStringConversion_ReturnsExpression()
    {
        string result = Condition.IsPrivileged;

        Assert.Equal("Privileged", result);
    }

    // ── MsiProperty integration ──

    [Fact]
    public void MsiProperty_GreaterThanOrEqual_ReturnsCondition()
    {
        var condition = MsiProperty.VersionNT >= 603;

        Assert.Equal("VersionNT >= 603", condition.ToString());
    }

    [Fact]
    public void MsiProperty_ComplexExpression_CombinesCorrectly()
    {
        var result = (Condition.Is64BitOS & (MsiProperty.Custom("DB_MODE") == "1"))
                     | MsiProperty.Custom("C") == 42;

        Assert.Equal(
            "((VersionNT64 OR Msix64) AND (DB_MODE = \"1\")) OR (C = 42)",
            result.ToString());
    }

    // ── Validation ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Property_NullOrEmpty_ThrowsArgumentException(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => Condition.Property(name!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Raw_NullOrEmpty_ThrowsArgumentException(string? expression)
    {
        Assert.ThrowsAny<ArgumentException>(() => Condition.Raw(expression!));
    }
}
