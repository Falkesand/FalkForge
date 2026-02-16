using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Decompiler.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsiDecompilerTests
{
    [Fact]
    public void Decompile_WithMockData_ReturnsPackageModel()
    {
        using var access = CreateStandardMockAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal("Test Application", result.Value.Name);
        Assert.Equal("Contoso", result.Value.Manufacturer);
        Assert.Equal(new Version(2, 1, 0), result.Value.Version);
    }

    [Fact]
    public void Decompile_ExtractsUpgradeCode()
    {
        using var access = CreateStandardMockAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789012"), result.Value.UpgradeCode);
    }

    [Fact]
    public void Decompile_ExtractsFiles()
    {
        using var access = CreateStandardMockAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Files);
        Assert.Equal("app.exe", result.Value.Files[0].FileName);
    }

    [Fact]
    public void Decompile_ExtractsFeatures()
    {
        using var access = CreateStandardMockAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Features);
        Assert.Equal("Complete", result.Value.Features[0].Id);
    }

    [Fact]
    public void Decompile_ExtractsRegistryEntries()
    {
        using var access = CreateStandardMockAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.RegistryEntries);
        Assert.Equal("SOFTWARE\\TestApp", result.Value.RegistryEntries[0].Key);
    }

    [Fact]
    public void Decompile_FileNotFound_ReturnsDec001Error()
    {
        var decompiler = new MsiDecompiler();

        var result = decompiler.Decompile("nonexistent.msi");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("DEC001", result.Error.Message);
    }

    [Fact]
    public void Decompile_EmptyPath_ReturnsDec001Error()
    {
        var decompiler = new MsiDecompiler();

        var result = decompiler.Decompile("");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC001", result.Error.Message);
    }

    [Fact]
    public void DecompileToCSharp_WithMockData_ReturnsCSharpSource()
    {
        using var access = CreateStandardMockAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.DecompileToCSharp("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Contains("Test Application", result.Value);
        Assert.Contains("Contoso", result.Value);
        Assert.Contains("builder.Build()", result.Value);
    }

    [Fact]
    public void DecompileToCSharp_FileNotFound_PropagatesError()
    {
        var decompiler = new MsiDecompiler();

        var result = decompiler.DecompileToCSharp("nonexistent.msi");

        Assert.True(result.IsFailure);
        Assert.Contains("DEC001", result.Error.Message);
    }

    [Fact]
    public void Decompile_EmptyDatabase_ReturnsMinimalPackage()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Empty App"],
                ["Manufacturer", "Nobody"],
                ["ProductVersion", "1.0.0"]
            ]);

        var decompiler = new MsiDecompiler(access);
        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal("Empty App", result.Value.Name);
        Assert.Equal("Nobody", result.Value.Manufacturer);
        Assert.Empty(result.Value.Files);
        Assert.Empty(result.Value.Features);
        Assert.Empty(result.Value.RegistryEntries);
    }

    [Fact]
    public void Decompile_NoPropertyTable_UsesDefaults()
    {
        using var access = new MockMsiTableAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal("Unknown", result.Value.Name);
        Assert.Equal("Unknown", result.Value.Manufacturer);
        Assert.Equal(new Version(1, 0, 0), result.Value.Version);
    }

    [Fact]
    public void Decompile_PerUserScope_DetectedFromAllusers()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Per User App"],
                ["Manufacturer", "Corp"],
                ["ProductVersion", "1.0.0"],
                ["ALLUSERS", "2"]
            ]);

        var decompiler = new MsiDecompiler(access);
        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal(InstallScope.PerUser, result.Value.Scope);
    }

    [Fact]
    public void Decompile_UnicodeValues_Preserved()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Anwendung"],
                ["Manufacturer", "Unternehmen"],
                ["ProductVersion", "1.0.0"]
            ])
            .WithTable("Registry",
            [
                ["reg1", "2", "SOFTWARE\\Unternehmen", "Beschreibung", "Deutsche Beschreibung", "comp1"]
            ]);

        var decompiler = new MsiDecompiler(access);
        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal("Anwendung", result.Value.Name);
        Assert.Single(result.Value.RegistryEntries);
        Assert.Equal("Deutsche Beschreibung", result.Value.RegistryEntries[0].Value);
    }

    private static MockMsiTableAccess CreateStandardMockAccess()
    {
        return new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Test Application"],
                ["Manufacturer", "Contoso"],
                ["ProductVersion", "2.1.0"],
                ["UpgradeCode", "{12345678-1234-1234-1234-123456789012}"],
                ["ProductCode", "{87654321-4321-4321-4321-210987654321}"]
            ])
            .WithTable("Directory",
            [
                ["TARGETDIR", null, "SourceDir"],
                ["ProgramFilesFolder", "TARGETDIR", "."],
                ["INSTALLFOLDER", "ProgramFilesFolder", "TestApp"]
            ])
            .WithTable("Component",
            [
                ["comp1", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "INSTALLFOLDER", "256", null, "file1"]
            ])
            .WithTable("File",
            [
                ["file1", "comp1", "APP~1|app.exe", "4096", "2.1.0", null, "0", "1"]
            ])
            .WithTable("Feature",
            [
                ["Complete", null, "Complete", "Full installation", "1", "1", "INSTALLFOLDER", "0"]
            ])
            .WithTable("FeatureComponents",
            [
                ["Complete", "comp1"]
            ])
            .WithTable("Registry",
            [
                ["reg1", "2", "SOFTWARE\\TestApp", "Version", "2.1.0", "comp1"]
            ]);
    }
}
