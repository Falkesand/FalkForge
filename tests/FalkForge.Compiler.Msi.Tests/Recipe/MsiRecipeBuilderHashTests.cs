using System;
using System.Collections.Generic;
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Integration tests verifying that <see cref="MsiRecipeBuilder.Build"/>
/// populates <see cref="MsiDatabaseRecipe.ContentHash"/> via
/// <see cref="RecipeContentHasher"/>. The hash must be deterministic for
/// equivalent inputs and must change when the resolved package changes.
/// </summary>
public sealed class MsiRecipeBuilderHashTests
{
    [Fact]
    public void Build_returns_recipe_with_non_empty_content_hash()
    {
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            MakeResolvedPackage("Test", "M"),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ContentHash.IsEmpty);
        Assert.Equal(32, result.Value.ContentHash.Length);
    }

    [Fact]
    public void Build_with_same_resolved_package_twice_returns_recipes_with_equal_content_hash()
    {
        ResolvedPackage resolved = MakeResolvedPackage("Test", "M");

        Result<MsiDatabaseRecipe> r1 = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Result<MsiDatabaseRecipe> r2 = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.True(r1.Value.ContentHash.Span.SequenceEqual(r2.Value.ContentHash.Span));
    }

    [Fact]
    public void Build_recipes_with_different_packages_return_different_content_hashes()
    {
        ResolvedPackage a = MakeResolvedPackage("ProductA", "M");
        ResolvedPackage b = MakeResolvedPackage("ProductB", "M");

        Result<MsiDatabaseRecipe> r1 = MsiRecipeBuilder.Build(
            a,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Result<MsiDatabaseRecipe> r2 = MsiRecipeBuilder.Build(
            b,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.False(r1.Value.ContentHash.Span.SequenceEqual(r2.Value.ContentHash.Span));
    }

    private static ResolvedPackage MakeResolvedPackage(string name, string manufacturer)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = name,
                Manufacturer = manufacturer,
                Version = new Version(1, 0, 0),
                Properties = new List<PropertyModel>
                {
                    new() { Name = "ProductCode", Value = "{" + name + "}" },
                },
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
