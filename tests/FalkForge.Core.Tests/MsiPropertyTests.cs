using System.Reflection;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class MsiPropertyTests
{
    // ── Static property instances return correct Name ──

    [Theory]
    [InlineData(nameof(MsiProperty.ProductName), "ProductName")]
    [InlineData(nameof(MsiProperty.ProductCode), "ProductCode")]
    [InlineData(nameof(MsiProperty.ProductVersion), "ProductVersion")]
    [InlineData(nameof(MsiProperty.ProductLanguage), "ProductLanguage")]
    [InlineData(nameof(MsiProperty.Manufacturer), "Manufacturer")]
    [InlineData(nameof(MsiProperty.UpgradeCode), "UpgradeCode")]
    [InlineData(nameof(MsiProperty.InstallFolder), "INSTALLFOLDER")]
    [InlineData(nameof(MsiProperty.InstallDir), "INSTALLDIR")]
    [InlineData(nameof(MsiProperty.TargetDir), "TARGETDIR")]
    [InlineData(nameof(MsiProperty.VersionNT), "VersionNT")]
    [InlineData(nameof(MsiProperty.VersionNT64), "VersionNT64")]
    [InlineData(nameof(MsiProperty.ServicePackLevel), "ServicePackLevel")]
    [InlineData(nameof(MsiProperty.WindowsBuildNumber), "WindowsBuildNumber")]
    [InlineData(nameof(MsiProperty.Privileged), "Privileged")]
    [InlineData(nameof(MsiProperty.AdminUser), "AdminUser")]
    [InlineData(nameof(MsiProperty.Installed), "Installed")]
    [InlineData(nameof(MsiProperty.UILevel), "UILevel")]
    [InlineData(nameof(MsiProperty.REMOVE), "REMOVE")]
    [InlineData(nameof(MsiProperty.ProgramFilesFolder), "ProgramFilesFolder")]
    [InlineData(nameof(MsiProperty.SystemFolder), "SystemFolder")]
    [InlineData(nameof(MsiProperty.ComputerName), "ComputerName")]
    [InlineData(nameof(MsiProperty.LogonUser), "LogonUser")]
    public void StaticProperty_HasCorrectName(string propertyName, string expectedName)
    {
        var prop = typeof(MsiProperty).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)!;
        var value = (MsiProperty)prop.GetValue(null)!;
        Assert.Equal(expectedName, value.Name);
    }

    // ── Custom factory ──

    [Fact]
    public void Custom_ReturnsInstanceWithGivenName()
    {
        var prop = MsiProperty.Custom("MY_PROP");

        Assert.Equal("MY_PROP", prop.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Custom_WithNullOrEmpty_ThrowsArgumentException(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => MsiProperty.Custom(name!));
    }

    // ── ToString ──

    [Fact]
    public void ToString_ReturnsBracketedName()
    {
        Assert.Equal("[INSTALLFOLDER]", MsiProperty.InstallFolder.ToString());
    }

    // ── / operator ──

    [Fact]
    public void SlashOperator_ConcatenatesSubPath()
    {
        var result = MsiProperty.InstallFolder / "bin";

        Assert.Equal("[INSTALLFOLDER]bin", result);
    }

    // ── Comparison operators returning Condition ──

    [Fact]
    public void GreaterThanOrEqual_ReturnsCorrectCondition()
    {
        var condition = MsiProperty.VersionNT >= 603;

        Assert.Equal("VersionNT >= 603", condition.ToString());
    }

    [Fact]
    public void GreaterThan_ReturnsCorrectCondition()
    {
        var condition = MsiProperty.VersionNT > 600;

        Assert.Equal("VersionNT > 600", condition.ToString());
    }

    [Fact]
    public void LessThanOrEqual_ReturnsCorrectCondition()
    {
        var condition = MsiProperty.VersionNT <= 603;

        Assert.Equal("VersionNT <= 603", condition.ToString());
    }

    [Fact]
    public void LessThan_ReturnsCorrectCondition()
    {
        var condition = MsiProperty.VersionNT < 600;

        Assert.Equal("VersionNT < 600", condition.ToString());
    }

    [Fact]
    public void EqualInt_ReturnsMsiSingleEquals()
    {
        var condition = MsiProperty.Custom("PROP") == 42;

        Assert.Equal("PROP = 42", condition.ToString());
    }

    [Fact]
    public void NotEqualInt_ReturnsMsiDiamondOperator()
    {
        var condition = MsiProperty.Custom("PROP") != 42;

        Assert.Equal("PROP <> 42", condition.ToString());
    }

    [Fact]
    public void EqualString_ReturnsQuotedValue()
    {
        var condition = MsiProperty.Custom("DB_MODE") == "production";

        Assert.Equal("DB_MODE = \"production\"", condition.ToString());
    }

    [Fact]
    public void NotEqualString_ReturnsQuotedValue()
    {
        var condition = MsiProperty.Custom("DB_MODE") != "test";

        Assert.Equal("DB_MODE <> \"test\"", condition.ToString());
    }

    // ── Equality ──

    [Fact]
    public void Equals_SameName_ReturnsTrue()
    {
        var a = MsiProperty.Custom("MY_PROP");
        var b = MsiProperty.Custom("MY_PROP");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentName_ReturnsFalse()
    {
        var a = MsiProperty.Custom("PROP_A");
        var b = MsiProperty.Custom("PROP_B");

        Assert.NotEqual(a, b);
    }

    // ── GetHashCode ──

    [Fact]
    public void GetHashCode_SameName_ReturnsSameHash()
    {
        var a = MsiProperty.Custom("MY_PROP");
        var b = MsiProperty.Custom("MY_PROP");

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
