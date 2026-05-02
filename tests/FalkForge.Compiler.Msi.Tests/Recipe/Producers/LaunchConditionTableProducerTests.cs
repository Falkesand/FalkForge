using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class LaunchConditionTableProducerTests
{
    [Fact]
    public void Schema_has_two_columns_condition_pk_no_foreign_keys()
    {
        LaunchConditionTableProducer producer = new();

        Assert.Equal("LaunchCondition", producer.Schema.Name.Value);
        Assert.Equal(2, producer.Schema.Columns.Length);
        Assert.Equal("Condition", producer.Schema.Columns[0].Name);
        Assert.Equal("Description", producer.Schema.Columns[1].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateLaunchConditionTable defines Condition as
        // CHAR(255) NOT NULL and Description as CHAR(255) NOT NULL LOCALIZABLE.
        // Catch any drift between the producer schema and the legacy DDL early.
        LaunchConditionTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.Localized, columns[1].Type);

        Assert.Equal(255, columns[0].Width);
        Assert.Equal(255, columns[1].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
    }

    [Fact]
    public void Produce_with_no_conditions_or_upgrade_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            launchConditions: Array.Empty<LaunchConditionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_launch_condition()
    {
        LaunchConditionModel a = new() { Condition = "VersionNT >= 600", Message = "Win Vista or later" };
        LaunchConditionModel b = new() { Condition = "Privileged", Message = "Run as admin" };
        ResolvedPackage resolved = MakeResolved(launchConditions: new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("VersionNT >= 600", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("Win Vista or later", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal("Privileged", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("Run as admin", ((CellValue.StringValue)rows[1].Cells[1]).Value);
    }

    [Fact]
    public void Produce_emits_downgrade_guard_when_upgrade_disallows_downgrades()
    {
        // Mirrors legacy EmitLaunchConditions: when package.Upgrade is set and
        // AllowDowngrades is false, prepend a NOT NEWERVERSIONFOUND condition
        // using DowngradeErrorMessage as the description.
        UpgradeModel upgrade = new()
        {
            AllowDowngrades = false,
            DowngradeErrorMessage = "Custom downgrade msg",
        };
        ResolvedPackage resolved = MakeResolved(
            launchConditions: Array.Empty<LaunchConditionModel>(),
            upgrade: upgrade);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("NOT NEWERVERSIONFOUND", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("Custom downgrade msg", ((CellValue.StringValue)row.Cells[1]).Value);
    }

    [Fact]
    public void Produce_skips_downgrade_guard_when_upgrade_allows_downgrades()
    {
        UpgradeModel upgrade = new() { AllowDowngrades = true };
        ResolvedPackage resolved = MakeResolved(
            launchConditions: Array.Empty<LaunchConditionModel>(),
            upgrade: upgrade);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_uses_default_message_when_upgrade_downgrade_message_is_null()
    {
        // The legacy EmitLaunchConditions falls back to the literal
        // "A newer version is already installed." when the upgrade does not
        // supply a custom DowngradeErrorMessage. Even though UpgradeModel's
        // default initializer also sets that string, the producer must not
        // assume the default was preserved.
        UpgradeModel upgrade = new()
        {
            AllowDowngrades = false,
            DowngradeErrorMessage = null,
        };
        ResolvedPackage resolved = MakeResolved(
            launchConditions: Array.Empty<LaunchConditionModel>(),
            upgrade: upgrade);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("A newer version is already installed.", ((CellValue.StringValue)row.Cells[1]).Value);
    }

    [Fact]
    public void Produce_emits_major_upgrade_downgrade_guard_with_custom_message()
    {
        // Mirrors legacy EmitLaunchConditions: when package.MajorUpgrade is
        // set and Downgrade is not configured to allow downgrades, emit a
        // NOT NEWERVERSIONFOUND condition using Downgrade.ErrorMessage when
        // present.
        MajorUpgradeModel major = new();
        DowngradeModel downgrade = new()
        {
            AllowDowngrades = false,
            ErrorMessage = "Block downgrade",
        };
        ResolvedPackage resolved = MakeResolved(
            launchConditions: Array.Empty<LaunchConditionModel>(),
            majorUpgrade: major,
            downgrade: downgrade);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("NOT NEWERVERSIONFOUND", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("Block downgrade", ((CellValue.StringValue)row.Cells[1]).Value);
    }

    [Fact]
    public void Produce_skips_major_upgrade_guard_when_downgrade_allows_downgrades()
    {
        MajorUpgradeModel major = new();
        DowngradeModel downgrade = new() { AllowDowngrades = true };
        ResolvedPackage resolved = MakeResolved(
            launchConditions: Array.Empty<LaunchConditionModel>(),
            majorUpgrade: major,
            downgrade: downgrade);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_uses_default_message_when_major_upgrade_downgrade_is_null()
    {
        MajorUpgradeModel major = new();
        ResolvedPackage resolved = MakeResolved(
            launchConditions: Array.Empty<LaunchConditionModel>(),
            majorUpgrade: major,
            downgrade: null);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("A newer version is already installed.", ((CellValue.StringValue)row.Cells[1]).Value);
    }

    [Fact]
    public void Produce_combines_guards_and_explicit_conditions_in_order()
    {
        // The legacy emitter inserts the Upgrade guard first, then the
        // MajorUpgrade guard, then iterates the explicit LaunchConditions.
        // Note the two guards both use NOT NEWERVERSIONFOUND so a downstream
        // PrimaryKeyValidator would catch the collision; emitting both is
        // still the legacy-exact behavior the producer must preserve.
        UpgradeModel upgrade = new() { AllowDowngrades = false, DowngradeErrorMessage = "U" };
        MajorUpgradeModel major = new();
        DowngradeModel downgrade = new() { AllowDowngrades = false, ErrorMessage = "M" };
        LaunchConditionModel explicitCondition = new() { Condition = "C1", Message = "E" };

        ResolvedPackage resolved = MakeResolved(
            launchConditions: new[] { explicitCondition },
            upgrade: upgrade,
            majorUpgrade: major,
            downgrade: downgrade);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
        Assert.Equal("NOT NEWERVERSIONFOUND", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("U", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal("NOT NEWERVERSIONFOUND", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("M", ((CellValue.StringValue)rows[1].Cells[1]).Value);
        Assert.Equal("C1", ((CellValue.StringValue)rows[2].Cells[0]).Value);
        Assert.Equal("E", ((CellValue.StringValue)rows[2].Cells[1]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        LaunchConditionTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<LaunchConditionModel> launchConditions,
        UpgradeModel? upgrade = null,
        MajorUpgradeModel? majorUpgrade = null,
        DowngradeModel? downgrade = null)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                LaunchConditions = launchConditions,
                Upgrade = upgrade,
                MajorUpgrade = majorUpgrade,
                Downgrade = downgrade,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
