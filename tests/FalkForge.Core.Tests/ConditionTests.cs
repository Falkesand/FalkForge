using FalkForge;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class ConditionTests
{
    // ── Static pre-composed conditions ──

    [Fact]
    public void Is64BitOS_ReturnsExpectedExpression()
    {
        Assert.Equal("VersionNT64 OR Msix64", Condition.Is64BitOS.ToString());
    }

    [Fact]
    public void IsPrivileged_ReturnsExpectedExpression()
    {
        Assert.Equal("Privileged", Condition.IsPrivileged.ToString());
    }

    [Fact]
    public void IsAdmin_ReturnsExpectedExpression()
    {
        Assert.Equal("AdminUser", Condition.IsAdmin.ToString());
    }

    [Fact]
    public void IsTerminalServer_ReturnsExpectedExpression()
    {
        Assert.Equal("TerminalServer", Condition.IsTerminalServer.ToString());
    }

    [Fact]
    public void IsWindows10OrLater_ReturnsExpectedExpression()
    {
        Assert.Equal("VersionNT >= 603", Condition.IsWindows10OrLater.ToString());
    }

    [Fact]
    public void IsWindows11OrLater_ReturnsExpectedExpression()
    {
        Assert.Equal("WindowsBuildNumber >= 22000", Condition.IsWindows11OrLater.ToString());
    }

    [Fact]
    public void IsInstalled_ReturnsExpectedExpression()
    {
        Assert.Equal("Installed", Condition.IsInstalled.ToString());
    }

    [Fact]
    public void IsInstalling_ReturnsExpectedExpression()
    {
        Assert.Equal("NOT Installed", Condition.IsInstalling.ToString());
    }

    [Fact]
    public void IsUninstalling_ReturnsExpectedExpression()
    {
        Assert.Equal("REMOVE=\"ALL\"", Condition.IsUninstalling.ToString());
    }

    [Fact]
    public void IsRepairing_ReturnsExpectedExpression()
    {
        Assert.Equal("REINSTALL", Condition.IsRepairing.ToString());
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
