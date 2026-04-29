using System;
using System.Collections.Generic;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class MsiRecipeBuilderProducersTests
{
    [Fact]
    public void Build_with_simple_resolved_package_emits_fifteen_tables_in_order()
    {
        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Value.Tables.Length);
    }

    [Fact]
    public void Build_table_order_is_fk_safe_topological()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        Assert.Equal("Property", recipe.Tables[0].Name.Value);
        Assert.Equal("Directory", recipe.Tables[1].Name.Value);
        Assert.Equal("Feature", recipe.Tables[2].Name.Value);
        Assert.Equal("Component", recipe.Tables[3].Name.Value);
        Assert.Equal("File", recipe.Tables[4].Name.Value);
        Assert.Equal("FeatureComponents", recipe.Tables[5].Name.Value);
        Assert.Equal("Condition", recipe.Tables[6].Name.Value);
        Assert.Equal("Upgrade", recipe.Tables[7].Name.Value);
        Assert.Equal("Media", recipe.Tables[8].Name.Value);
        Assert.Equal("Registry", recipe.Tables[9].Name.Value);
        Assert.Equal("ServiceInstall", recipe.Tables[10].Name.Value);
        Assert.Equal("Shortcut", recipe.Tables[11].Name.Value);
        Assert.Equal("Environment", recipe.Tables[12].Name.Value);
        Assert.Equal("MoveFile", recipe.Tables[13].Name.Value);
        Assert.Equal("RemoveFile", recipe.Tables[14].Name.Value);
    }

    [Fact]
    public void Build_each_table_has_create_and_insert_sql_populated()
    {
        MsiDatabaseRecipe recipe = MsiRecipeBuilder.Build(
            MakeResolvedPackage(),
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions()).Value;

        foreach (RecipeTable table in recipe.Tables)
        {
            Assert.False(string.IsNullOrWhiteSpace(table.CreateTableSql));
            Assert.False(string.IsNullOrWhiteSpace(table.InsertViewSql));
            Assert.StartsWith("SELECT", table.InsertViewSql, StringComparison.Ordinal);
        }
    }

    private static ResolvedPackage MakeResolvedPackage()
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "Test",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
