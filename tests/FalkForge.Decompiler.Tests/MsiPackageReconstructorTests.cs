using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Unit tests for MsiPackageReconstructor. Pure cross-platform — no msi.dll,
/// no file system. Tests build a PropertySet directly from schema rows.
/// </summary>
public sealed class MsiPackageReconstructorTests
{
    private static PropertySet MakeProps(params (string Key, string Value)[] pairs)
    {
        var rows = pairs.Select(p => new PropertyRow(p.Key, p.Value)).ToList();
        return PropertySet.From(rows);
    }

    [Fact]
    public void Rebuild_ExtractsProductName()
    {
        var props = MakeProps(("ProductName", "Acme"), ("Manufacturer", "Acme Corp"), ("ProductVersion", "1.2.3"));
        var result = MsiPackageReconstructor.ExtractMetadata(props);
        Assert.Equal("Acme", result.Name);
    }

    [Fact]
    public void Rebuild_ExtractsManufacturer()
    {
        var props = MakeProps(("ProductName", "X"), ("Manufacturer", "Widgets Inc"), ("ProductVersion", "1.0.0"));
        var result = MsiPackageReconstructor.ExtractMetadata(props);
        Assert.Equal("Widgets Inc", result.Manufacturer);
    }

    [Fact]
    public void Rebuild_ParsesVersion()
    {
        var props = MakeProps(("ProductName", "X"), ("Manufacturer", "Y"), ("ProductVersion", "3.4.5"));
        var result = MsiPackageReconstructor.ExtractMetadata(props);
        Assert.Equal(new Version(3, 4, 5), result.Version);
    }

    [Fact]
    public void Rebuild_MissingVersion_DefaultsTo100()
    {
        var props = MakeProps(("ProductName", "X"), ("Manufacturer", "Y"));
        var result = MsiPackageReconstructor.ExtractMetadata(props);
        Assert.Equal(new Version(1, 0, 0), result.Version);
    }

    [Fact]
    public void Rebuild_ParsesUpgradeCode()
    {
        var guid = Guid.NewGuid();
        var props = MakeProps(
            ("ProductName", "X"), ("Manufacturer", "Y"), ("ProductVersion", "1.0.0"),
            ("UpgradeCode", guid.ToString("B")));
        var result = MsiPackageReconstructor.ExtractMetadata(props);
        Assert.Equal(guid, result.UpgradeCode);
    }

    [Fact]
    public void Rebuild_ALLUSERS_Empty_IsPerUser()
    {
        var props = MakeProps(
            ("ProductName", "X"), ("Manufacturer", "Y"), ("ProductVersion", "1.0.0"),
            ("ALLUSERS", ""));
        var result = MsiPackageReconstructor.ExtractMetadata(props);
        Assert.Equal(InstallScope.PerUser, result.Scope);
    }

    [Fact]
    public void Rebuild_ALLUSERS_1_IsPerMachine()
    {
        var props = MakeProps(
            ("ProductName", "X"), ("Manufacturer", "Y"), ("ProductVersion", "1.0.0"),
            ("ALLUSERS", "1"));
        var result = MsiPackageReconstructor.ExtractMetadata(props);
        Assert.Equal(InstallScope.PerMachine, result.Scope);
    }
}
