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
        // Phase 4 wires in thirty-five built-in producers (Property,
        // Directory, Feature, Component, File, FeatureComponents, Condition,
        // Upgrade, Media, Registry, RemoveRegistry, ServiceInstall,
        // ServiceControl, Shortcut, Environment, Font, LaunchCondition,
        // IniFile, CreateFolder, DuplicateFile, Binary, CustomAction,
        // LockPermissions, MsiLockPermissionsEx, MIME, ProgId, Extension,
        // Class, TypeLib, MsiAssembly, MsiAssemblyName, Verb, MoveFile,
        // RemoveFile, InstallUISequence).
        // With an empty resolved package each producer emits zero rows but the
        // table itself is still present so downstream phases see a stable table
        // set. The recipe's Tables array therefore contains thirty-five tables,
        // not zero.
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.False(recipe.Tables.IsDefault);
        Assert.Equal(35, recipe.Tables.Length);
        foreach (RecipeTable table in recipe.Tables)
        {
            // Media always emits a single header row even when the resolved
            // package has no files. Directory unconditionally emits the
            // implicit TARGETDIR root row so msi.dll has a valid Formatted
            // anchor. Property synthesizes the MSI built-ins (ProductName,
            // Manufacturer, ProductVersion, ProductCode, UpgradeCode,
            // ProductLanguage, ALLUSERS) from the package headline fields,
            // matching legacy TableEmitter.EmitProperties. Every other
            // producer is data-driven and emits zero rows.
            if (table.Name.Value == "Media")
            {
                Assert.Single(table.Rows);
            }
            else if (table.Name.Value == "Directory")
            {
                Assert.Single(table.Rows);
            }
            else if (table.Name.Value == "Property")
            {
                // 7 built-ins for a default per-machine package: ProductName,
                // Manufacturer, ProductVersion, ProductCode, UpgradeCode,
                // ProductLanguage, ALLUSERS. EnableRestartManager is false so
                // MSIRMSHUTDOWN is omitted.
                Assert.Equal(7, table.Rows.Length);
            }
            else
            {
                Assert.True(table.Rows.IsEmpty);
            }
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
    public void Build_empty_pipeline_populates_content_hash()
    {
        // Phase 6 wires RecipeContentHasher into the builder. Even an empty
        // resolved package produces a non-empty 32-byte SHA-256 digest over
        // the canonical recipe content (table identities, schema metadata,
        // summary info, etc.). The digest is stable across repeated builds.
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.False(recipe.ContentHash.IsEmpty);
        Assert.Equal(32, recipe.ContentHash.Length);
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
