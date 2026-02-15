using FalkInstaller.Decompiler.TableReaders;
using Xunit;

namespace FalkInstaller.Decompiler.Tests;

public sealed class UpgradeTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsNullMajorUpgrade()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Upgrade", []);

        var result = UpgradeTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.MajorUpgrade);
    }

    [Fact]
    public void Read_MissingTable_ReturnsNullMajorUpgrade()
    {
        using var access = new MockMsiTableAccess();

        var result = UpgradeTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.MajorUpgrade);
    }

    [Fact]
    public void Read_WithUpgradeRows_ReturnsMajorUpgradeModel()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Upgrade",
            [
                // UpgradeCode, VersionMin, VersionMax, Language, Attributes, Remove, ActionProperty
                ["{12345678-1234-1234-1234-123456789012}", "1.0.0", "2.0.0", null, "1", null, "PREVIOUSVERSIONS"]
            ]);

        var result = UpgradeTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MajorUpgrade);
        Assert.True(result.Value.MajorUpgrade.MigrateFeatures);
    }

    [Fact]
    public void Read_VersionMaxInclusive_AllowsSameVersion()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Upgrade",
            [
                ["{12345678-1234-1234-1234-123456789012}", "1.0.0", "2.0.0", null, "512", null, "PREVIOUSVERSIONS"]
            ]);

        var result = UpgradeTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MajorUpgrade);
        Assert.True(result.Value.MajorUpgrade.AllowSameVersionUpgrades);
    }
}
