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
        Assert.Equal(36, recipe.Tables.Length);
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
            else if (table.Name.Value == "InstallExecuteSequence")
            {
                // InstallExecuteSequence always emits its unconditional baseline
                // (21 rows) regardless of how empty the package is, matching the
                // legacy TableEmitter.EmitInstallSequences baseline list.
                Assert.NotEmpty(table.Rows);
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
    public void Build_empty_pipeline_has_empty_cabinet_embeddings()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.True(recipe.CabinetEmbeddings.IsEmpty);
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
    public void Build_summary_info_is_fully_populated_from_package()
    {
        // Phase 9 Step 2: SummaryInfoRecipe must be fully populated in
        // MsiRecipeBuilder.BuildCore so the recipe alone is the single source
        // of truth for the OLE summary stream — no post-apply patch required.
        var productCode = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "MyProduct",
                Manufacturer = "Acme Corp",
                Version = new Version(2, 0, 0),
                ProductCode = productCode,
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };

        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        SummaryInfoRecipe info = recipe.SummaryInfo;
        Assert.Equal("Installation Database", info.Title);
        Assert.Equal("MyProduct", info.Subject);
        Assert.Equal("Acme Corp", info.Author);
        Assert.Equal("Installer", info.Keywords);
        // Comments default: no Description on package → generated from Name.
        Assert.Contains("MyProduct", info.Comments, StringComparison.Ordinal);
        // Template defaults to x64;1033 for ProcessorArchitecture.X64.
        Assert.Equal("x64;1033", info.Template);
        // RevisionNumber is the ProductCode GUID in upper-case registry-format braces.
        Assert.Equal(productCode.ToString("B").ToUpperInvariant(), info.RevisionNumber);
        Assert.Equal(1252, info.CodePage);
        Assert.Equal("FalkForge", info.CreatingApplication);
        Assert.Equal(2, info.WordCount);
        Assert.Equal(200, info.PageCount);
        Assert.Equal(2, info.Security);
    }

    [Fact]
    public void Build_summary_info_uses_package_description_when_provided()
    {
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "MyProduct",
                Manufacturer = "Acme Corp",
                Version = new Version(1, 0, 0),
                Description = "Custom description text.",
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };

        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.Equal("Custom description text.", recipe.SummaryInfo.Comments);
    }

    [Fact]
    public void Build_summary_info_template_reflects_processor_architecture()
    {
        ResolvedPackage x86Resolved = new()
        {
            Package = new PackageModel
            {
                Name = "P",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Architecture = ProcessorArchitecture.X86,
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };

        MsiDatabaseRecipe x86Recipe = MsiRecipeBuilder.Build(
            x86Resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.Equal("Intel;1033", x86Recipe.SummaryInfo.Template);
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
