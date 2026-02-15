using Xunit;

namespace FalkForge.Core.Tests;

public sealed class MsiPropertyTests
{
    // ── Static property instances return correct Name ──

    [Fact]
    public void ProductName_HasCorrectName()
    {
        Assert.Equal("ProductName", MsiProperty.ProductName.Name);
    }

    [Fact]
    public void ProductCode_HasCorrectName()
    {
        Assert.Equal("ProductCode", MsiProperty.ProductCode.Name);
    }

    [Fact]
    public void ProductVersion_HasCorrectName()
    {
        Assert.Equal("ProductVersion", MsiProperty.ProductVersion.Name);
    }

    [Fact]
    public void ProductLanguage_HasCorrectName()
    {
        Assert.Equal("ProductLanguage", MsiProperty.ProductLanguage.Name);
    }

    [Fact]
    public void Manufacturer_HasCorrectName()
    {
        Assert.Equal("Manufacturer", MsiProperty.Manufacturer.Name);
    }

    [Fact]
    public void UpgradeCode_HasCorrectName()
    {
        Assert.Equal("UpgradeCode", MsiProperty.UpgradeCode.Name);
    }

    [Fact]
    public void InstallFolder_HasCorrectName()
    {
        Assert.Equal("INSTALLFOLDER", MsiProperty.InstallFolder.Name);
    }

    [Fact]
    public void InstallDir_HasCorrectName()
    {
        Assert.Equal("INSTALLDIR", MsiProperty.InstallDir.Name);
    }

    [Fact]
    public void TargetDir_HasCorrectName()
    {
        Assert.Equal("TARGETDIR", MsiProperty.TargetDir.Name);
    }

    [Fact]
    public void VersionNT_HasCorrectName()
    {
        Assert.Equal("VersionNT", MsiProperty.VersionNT.Name);
    }

    [Fact]
    public void VersionNT64_HasCorrectName()
    {
        Assert.Equal("VersionNT64", MsiProperty.VersionNT64.Name);
    }

    [Fact]
    public void ServicePackLevel_HasCorrectName()
    {
        Assert.Equal("ServicePackLevel", MsiProperty.ServicePackLevel.Name);
    }

    [Fact]
    public void WindowsBuildNumber_HasCorrectName()
    {
        Assert.Equal("WindowsBuildNumber", MsiProperty.WindowsBuildNumber.Name);
    }

    [Fact]
    public void Privileged_HasCorrectName()
    {
        Assert.Equal("Privileged", MsiProperty.Privileged.Name);
    }

    [Fact]
    public void AdminUser_HasCorrectName()
    {
        Assert.Equal("AdminUser", MsiProperty.AdminUser.Name);
    }

    [Fact]
    public void Installed_HasCorrectName()
    {
        Assert.Equal("Installed", MsiProperty.Installed.Name);
    }

    [Fact]
    public void UILevel_HasCorrectName()
    {
        Assert.Equal("UILevel", MsiProperty.UILevel.Name);
    }

    [Fact]
    public void REMOVE_HasCorrectName()
    {
        Assert.Equal("REMOVE", MsiProperty.REMOVE.Name);
    }

    [Fact]
    public void ProgramFilesFolder_HasCorrectName()
    {
        Assert.Equal("ProgramFilesFolder", MsiProperty.ProgramFilesFolder.Name);
    }

    [Fact]
    public void SystemFolder_HasCorrectName()
    {
        Assert.Equal("SystemFolder", MsiProperty.SystemFolder.Name);
    }

    [Fact]
    public void ComputerName_HasCorrectName()
    {
        Assert.Equal("ComputerName", MsiProperty.ComputerName.Name);
    }

    [Fact]
    public void LogonUser_HasCorrectName()
    {
        Assert.Equal("LogonUser", MsiProperty.LogonUser.Name);
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
