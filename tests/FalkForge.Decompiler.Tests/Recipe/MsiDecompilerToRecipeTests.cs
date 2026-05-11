using System.Runtime.Versioning;
using FalkForge.Decompiler.Recipe;
using Xunit;

namespace FalkForge.Decompiler.Tests.Recipe;

/// <summary>
/// Tests for <see cref="MsiDecompiler.DecompileToRecipe"/> — the entry point that
/// returns raw table-row collections without running the reconstructor stage.
/// All tests use <see cref="MockMsiTableAccess"/> so they run on any OS.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiDecompilerToRecipeTests
{
    [Fact]
    public void DecompileToRecipe_WithPropertyTable_ReturnsRecipeWithProductNameRow()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName",    "AcmeApp"],
                ["Manufacturer",   "Acme Corp"],
                ["ProductVersion", "2.3.1"],
            ]);

        var decompiler = new MsiDecompiler(access);
        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        var recipe = result.Value;
        Assert.Contains(recipe.Properties, r => r.Property == "ProductName" && r.Value == "AcmeApp");
    }

    [Fact]
    public void DecompileToRecipe_EmptyDatabase_ReturnsEmptyCollections()
    {
        using var access = new MockMsiTableAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Properties);
        Assert.Empty(result.Value.Files);
        Assert.Empty(result.Value.Features);
        Assert.Empty(result.Value.Components);
    }

    [Fact]
    public void DecompileToRecipe_EmptyPath_ReturnsDec001Error()
    {
        var decompiler = new MsiDecompiler();
        var result = decompiler.DecompileToRecipe("");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("DEC001", result.Error.Message);
    }

    [Fact]
    public void DecompileToRecipe_FileNotFound_ReturnsDec001Error()
    {
        var decompiler = new MsiDecompiler();
        var result = decompiler.DecompileToRecipe("nonexistent.msi");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("DEC001", result.Error.Message);
    }

    [Fact]
    public void DecompileToRecipe_WithMultipleTables_PopulatesAllCollections()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName",    "BigApp"],
                ["Manufacturer",   "BigCorp"],
                ["ProductVersion", "1.0.0"],
            ])
            .WithTable("Component",
            [
                ["comp1", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "INSTALLFOLDER", "256", null, "file1"],
            ])
            .WithTable("File",
            [
                ["file1", "comp1", "APP~1|app.exe", "4096", "1.0.0", null, "0", "1"],
            ])
            .WithTable("Feature",
            [
                ["Complete", null, "Complete", "Full installation", "1", "1", "INSTALLFOLDER", "0"],
            ]);

        var decompiler = new MsiDecompiler(access);
        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Properties.Count);
        Assert.Single(result.Value.Components);
        Assert.Single(result.Value.Files);
        Assert.Single(result.Value.Features);
    }

    [Fact]
    public void DecompileToRecipe_DoesNotRunReconstructor()
    {
        // Verifies DecompileToRecipe skips MsiPackageReconstructor by succeeding
        // even when registry data that would confuse reconstruction is present.
        using var access = new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Test"],
            ])
            .WithTable("Registry",
            [
                // Malformed row: valid for raw read but would need component lookup in reconstructor
                ["reg1", "2", "SOFTWARE\\Test", "Key", "Val", "orphan_comp"],
            ]);

        var decompiler = new MsiDecompiler(access);
        var recipeResult = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(recipeResult.IsSuccess);
        Assert.Single(recipeResult.Value.RegistryEntries);
        // Property table should still have ProductName
        Assert.Contains(recipeResult.Value.Properties, p => p.Property == "ProductName");
    }
}
