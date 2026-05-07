using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests.Schemas;

public sealed class UpgradeSchemaTests
{
    [Fact]
    public void Read_FullRow_MapsAllFields()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Upgrade",
            [
                ["{AAAABBBB-CCCC-DDDD-EEEE-FFFFAAAABBBB}", "1.0.0", "2.0.0", "1033", "769", "ALL", "WIX_UPGRADE_DETECTED"]
            ]);

        var result = TableReadEngine.ReadOne(UpgradeSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        var row = result.Value[0];
        Assert.Equal("{AAAABBBB-CCCC-DDDD-EEEE-FFFFAAAABBBB}", row.UpgradeCode);
        Assert.Equal("1.0.0", row.VersionMin);
        Assert.Equal("2.0.0", row.VersionMax);
        Assert.Equal("1033", row.Language);
        Assert.Equal(769, row.Attributes);
        Assert.Equal("ALL", row.Remove);
        Assert.Equal("WIX_UPGRADE_DETECTED", row.ActionProperty);
    }

    [Fact]
    public void Read_NullableVersionMinMax_Allowed()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Upgrade",
            [
                ["{AAAABBBB-CCCC-DDDD-EEEE-FFFFAAAABBBB}", null, null, null, "2", null, "NEWVER_FOUND"]
            ]);

        var result = TableReadEngine.ReadOne(UpgradeSchema.Schema, access);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].VersionMin);
        Assert.Null(result.Value[0].VersionMax);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmpty()
    {
        using var access = new MockMsiTableAccess();
        var result = TableReadEngine.ReadOne(UpgradeSchema.Schema, access);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
