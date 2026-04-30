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
    public void Build_propagates_pk_validator_failure_when_duplicate_feature()
    {
        // Two FeatureModel rows with the same Id produce two Feature table
        // rows with the same PK; PrimaryKeyValidator must reject the recipe
        // and MsiRecipeBuilder.Build must surface that failure.
        // (Property table built-in synthesis collapses same-named
        // PropertyModel entries via dictionary semantics — matching legacy
        // TableEmitter.EmitProperties — so it is no longer a viable PK
        // duplication driver.)
        PackageModel package = new()
        {
            Name = "Test",
            Manufacturer = "M",
            Version = new System.Version(1, 0, 0),
            Features = new List<FeatureModel>
            {
                new() { Id = "Dup", Title = "First", Description = "F" },
                new() { Id = "Dup", Title = "Second", Description = "S" },
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
        Assert.Contains("Feature", result.Error.Message, System.StringComparison.Ordinal);
        Assert.Contains("Dup", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Build_propagates_fk_validator_failure_when_orphan_feature_ref()
    {
        // A Component referencing a non-existent Feature ("Complete" — the
        // FeatureComponents producer's fallback name when the package
        // declares no features) produces a FeatureComponents row whose
        // Feature_ column points at a missing Feature primary key.
        // ForeignKeyValidator must reject the recipe and
        // MsiRecipeBuilder.Build must surface that failure.
        // Note: as of phase 4b the Directory tree is fully synthesized inside
        // DirectoryTableProducer, so a component referencing
        // KnownFolder.ProgramFiles is no longer an orphan FK — the producer
        // materializes ProgramFilesFolder automatically. This test now
        // exercises the FeatureComponents orphan path instead.
        PackageModel package = new()
        {
            Name = "Test",
            Manufacturer = "M",
            Version = new System.Version(1, 0, 0),
            Features = new List<FeatureModel>(),
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
        Assert.Contains("FeatureComponents", result.Error.Message, System.StringComparison.Ordinal);
    }
}
