using System;
using System.Collections.Generic;
using FalkForge;
using FalkForge.Builders;
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

    /// <summary>
    ///     Review finding (sonnet MED-2): two DISTINCT <see cref="ResolvedPackage"/>
    ///     instances with identical content in reproducible mode must produce
    ///     byte-equal <see cref="MsiDatabaseRecipe.ContentHash"/>.
    ///     WHY: guards that <see cref="ResolvedPackage.InstanceId"/> — the per-instance
    ///     nonce used in normal mode — never leaks into the reproducible digest.
    ///     If InstanceId contaminated the reproducible path, two independent build
    ///     invocations would produce different ContentHash values even for identical
    ///     inputs, breaking the reproducibility guarantee.
    ///
    ///     Expected GREEN today (the nonce is already gated on ReproducibleOptions).
    ///     This test is a regression guard against future refactors.
    /// </summary>
    [Fact]
    public void Reproducible_SameInputs_TwoInstances_SameContentHash()
    {
        // Two DISTINCT ResolvedPackage instances — different InstanceId values —
        // but identical Package content and the same reproducible epoch.
        var productCode = new Guid("FFFF0000-0000-0000-0000-000000000006");
        const long epoch = 1577836800L; // 2020-01-01T00:00:00Z

        ResolvedPackage instance1 = MakeReproduciblePackage(productCode, epoch);
        ResolvedPackage instance2 = MakeReproduciblePackage(productCode, epoch);

        // Confirm the instances are distinct objects with different InstanceIds.
        Assert.NotSame(instance1, instance2);
        Assert.NotEqual(instance1.InstanceId, instance2.InstanceId);

        Result<MsiDatabaseRecipe> r1 = MsiRecipeBuilder.Build(
            instance1, new List<IMsiTableContributor>(), new MsiRecipeBuildOptions());

        Result<MsiDatabaseRecipe> r2 = MsiRecipeBuilder.Build(
            instance2, new List<IMsiTableContributor>(), new MsiRecipeBuildOptions());

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);

        // Both RecipeContentHash values must be byte-equal — InstanceId must not
        // contaminate the reproducible-mode digest path.
        Assert.True(
            r1.Value.ContentHash.Span.SequenceEqual(r2.Value.ContentHash.Span),
            "ContentHash differed between two distinct ResolvedPackage instances with " +
            "identical reproducible inputs — InstanceId may have leaked into the digest.");
    }

    private static ResolvedPackage MakeReproduciblePackage(Guid productCode, long epoch)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "ReproTest",
                Manufacturer = "FalkForge Tests",
                Version = new Version(2, 0, 0),
                ProductCode = productCode,
                ReproducibleOptions = new ReproducibleBuildOptions { SourceDateEpoch = epoch },
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
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
