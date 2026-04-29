using System.Collections.Generic;
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Integration tests covering the wiring of <see cref="PrimaryKeyValidator"/>
/// and <see cref="ForeignKeyValidator"/> into <see cref="MsiRecipeBuilder.Build"/>.
/// These tests construct a <see cref="ResolvedPackage"/> whose producer output
/// triggers each validator and assert that the builder propagates the failure
/// instead of returning a recipe.
/// </summary>
public sealed class MsiRecipeBuilderValidatorIntegrationTests
{
    [Fact]
    public void Build_propagates_pk_validator_failure_when_duplicate_property()
    {
        // Two PropertyModel rows with the same Name produce two Property
        // table rows with the same PK; PrimaryKeyValidator must reject the
        // recipe and MsiRecipeBuilder.Build must surface that failure.
        PackageModel package = new()
        {
            Name = "Test",
            Manufacturer = "M",
            Version = new System.Version(1, 0, 0),
            Properties = new List<PropertyModel>
            {
                new() { Name = "ProductCode", Value = "{A}" },
                new() { Name = "ProductCode", Value = "{B}" },
            },
        };

        ResolvedPackage resolved = new()
        {
            Package = package,
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Property", result.Error.Message, System.StringComparison.Ordinal);
        Assert.Contains("ProductCode", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Build_propagates_fk_validator_failure_when_orphan_directory_ref()
    {
        // A Component whose Directory.Root token is not in the package's
        // Directories list produces a Component row with an orphan FK into
        // the Directory table. ForeignKeyValidator must reject the recipe
        // and MsiRecipeBuilder.Build must surface that failure.
        PackageModel package = new()
        {
            Name = "Test",
            Manufacturer = "M",
            Version = new System.Version(1, 0, 0),
            Directories = new List<DirectoryModel>(),
        };

        ResolvedComponent component = new()
        {
            Id = "Comp1",
            Guid = System.Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = "key1",
            Files = new List<ResolvedFile>(),
        };

        ResolvedPackage resolved = new()
        {
            Package = package,
            Components = new List<ResolvedComponent> { component },
            Files = new List<ResolvedFile>(),
        };

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Component", result.Error.Message, System.StringComparison.Ordinal);
        Assert.Contains("ProgramFilesFolder", result.Error.Message, System.StringComparison.Ordinal);
    }
}
