using System;
using System.Collections.Generic;
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class MsiRecipeBuilderTests
{
    [Fact]
    public void Build_returns_failure_on_null_resolved_package()
    {
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved: null!,
            contributors: new List<IMsiTableContributor>(),
            options: new MsiRecipeBuildOptions());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Build_returns_failure_on_null_contributors()
    {
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved: MakeResolvedPackage(),
            contributors: null!,
            options: new MsiRecipeBuildOptions());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Build_returns_failure_on_null_options()
    {
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved: MakeResolvedPackage(),
            contributors: new List<IMsiTableContributor>(),
            options: null!);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Build_with_empty_resolved_package_succeeds()
    {
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Build_empty_pipeline_emits_built_in_tables_with_no_rows()
    {
        // Phase 4 wires in five built-in producers (Property/Directory/Component/
        // File/Feature). With an empty resolved package each producer emits zero
        // rows but the table itself is still present so downstream phases see a
        // stable table set. The recipe's Tables array therefore contains five
        // tables, not zero.
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.False(recipe.Tables.IsDefault);
        Assert.Equal(5, recipe.Tables.Length);
        foreach (RecipeTable table in recipe.Tables)
        {
            Assert.True(table.Rows.IsEmpty);
        }
    }

    [Fact]
    public void Build_empty_pipeline_emits_empty_streams()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.Empty(recipe.Streams);
    }

    [Fact]
    public void Build_empty_pipeline_emits_empty_file_sequencing()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.True(recipe.FileSequencing.IsEmpty);
        Assert.False(recipe.FileSequencing.IsDefault);
    }

    [Fact]
    public void Build_empty_pipeline_has_null_cabinet_embedding()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.Null(recipe.CabinetEmbedding);
    }

    [Fact]
    public void Build_empty_pipeline_has_empty_content_hash()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.True(recipe.ContentHash.IsEmpty);
    }

    [Fact]
    public void Build_default_summary_info_has_empty_strings_and_zero_revision()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        SummaryInfoRecipe info = recipe.SummaryInfo;
        Assert.Equal(string.Empty, info.Title);
        Assert.Equal(string.Empty, info.Subject);
        Assert.Equal(string.Empty, info.Author);
        Assert.Equal(string.Empty, info.Template);
        Assert.Equal(string.Empty, info.Keywords);
        Assert.Equal(string.Empty, info.Comments);
        Assert.Equal(0, info.RevisionNumber);
        Assert.Equal(1252, info.CodePage);
    }

    private static ResolvedPackage MakeResolvedPackage()
    {
        return new ResolvedPackage
        {
            Package = new PackageModel { Name = "Test", Manufacturer = "M", Version = new Version(1, 0, 0) },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
