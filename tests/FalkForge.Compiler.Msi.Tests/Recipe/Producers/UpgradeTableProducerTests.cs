using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class UpgradeTableProducerTests
{
    [Fact]
    public void Schema_has_seven_columns_composite_pk_no_fks()
    {
        UpgradeTableProducer producer = new();

        Assert.Equal("Upgrade", producer.Schema.Name.Value);
        Assert.Equal(7, producer.Schema.Columns.Length);
        Assert.Equal("UpgradeCode", producer.Schema.Columns[0].Name);
        Assert.Equal("VersionMin", producer.Schema.Columns[1].Name);
        Assert.Equal("VersionMax", producer.Schema.Columns[2].Name);
        Assert.Equal("Language", producer.Schema.Columns[3].Name);
        Assert.Equal("Attributes", producer.Schema.Columns[4].Name);
        Assert.Equal("Remove", producer.Schema.Columns[5].Name);
        Assert.Equal("ActionProperty", producer.Schema.Columns[6].Name);

        // Composite PK matches MsiTableDefinitions.CreateUpgradeTable: UpgradeCode, VersionMin, VersionMax, Language, Attributes
        Assert.Equal(5, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
        Assert.Equal(2, producer.Schema.PrimaryKey[2].Value);
        Assert.Equal(3, producer.Schema.PrimaryKey[3].Value);
        Assert.Equal(4, producer.Schema.PrimaryKey[4].Value);

        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Produce_with_no_upgrade_or_major_upgrade_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(upgrade: null, majorUpgrade: null);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_upgrade_default_emits_older_256_and_newer_258_rows()
    {
        // Pins the Upgrade-branch attribute pair (256 / 258) after the
        // MakeRow helper extraction. EmitUpgrade encodes NEWERVERSIONFOUND
        // with msidbUpgradeAttributesOnlyDetect | VersionMinInclusive
        // (2 | 256 = 258) — distinct from the MajorUpgrade newer-row mask
        // of 2 — and a regression here would silently produce an MSI that
        // either failed to detect newer versions or wrongly removed them.
        ResolvedPackage resolved = MakeResolved(
            upgrade: new UpgradeModel { AllowDowngrades = false },
            majorUpgrade: null,
            version: new Version(1, 2, 3));

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal(256, ((CellValue.IntValue)rows[0].Cells[4]).Value);
        Assert.Equal("OLDERVERSIONFOUND", ((CellValue.StringValue)rows[0].Cells[6]).Value);
        Assert.Equal(258, ((CellValue.IntValue)rows[1].Cells[4]).Value);
        Assert.Equal("NEWERVERSIONFOUND", ((CellValue.StringValue)rows[1].Cells[6]).Value);
    }

    [Fact]
    public void Produce_upgrade_with_allow_downgrades_emits_only_older_row()
    {
        // The Upgrade branch (UpgradeModel.AllowDowngrades=true) must skip
        // the NEWERVERSIONFOUND row entirely — same shape as
        // EmitUpgrade line 56-69. Pin the suppression so a future helper
        // refactor cannot accidentally emit both rows.
        ResolvedPackage resolved = MakeResolved(
            upgrade: new UpgradeModel { AllowDowngrades = true },
            majorUpgrade: null);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal(256, ((CellValue.IntValue)row.Cells[4]).Value);
        Assert.Equal("OLDERVERSIONFOUND", ((CellValue.StringValue)row.Cells[6]).Value);
    }

    [Fact]
    public void Produce_major_upgrade_default_emits_two_rows_with_attribute_256()
    {
        // Mirrors EmitMajorUpgrade: OLDERVERSIONFOUND row spans 0.0.0 ..< current
        // version with attribute 256 (msidbUpgradeAttributesVersionMinInclusive
        // alone) when AllowSameVersionUpgrades is false. Without an explicit
        // Downgrade allow-list a NEWERVERSIONFOUND row is appended with
        // attribute 2 (msidbUpgradeAttributesOnlyDetect).
        Guid code = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Version version = new(2, 0, 0);
        ResolvedPackage resolved = MakeResolved(
            upgrade: null,
            majorUpgrade: new MajorUpgradeModel(),
            upgradeCode: code,
            version: version);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        string formatted = code.ToString("B").ToUpperInvariant();

        Assert.Equal(formatted, ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("0.0.0", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal("2.0.0", ((CellValue.StringValue)rows[0].Cells[2]).Value);
        Assert.Equal(256, ((CellValue.IntValue)rows[0].Cells[4]).Value);
        Assert.Equal("OLDERVERSIONFOUND", ((CellValue.StringValue)rows[0].Cells[6]).Value);

        Assert.Equal(formatted, ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("2.0.0", ((CellValue.StringValue)rows[1].Cells[1]).Value);
        Assert.Equal(string.Empty, ((CellValue.StringValue)rows[1].Cells[2]).Value);
        Assert.Equal(2, ((CellValue.IntValue)rows[1].Cells[4]).Value);
        Assert.Equal("NEWERVERSIONFOUND", ((CellValue.StringValue)rows[1].Cells[6]).Value);
    }

    [Fact]
    public void Produce_major_upgrade_allow_same_version_upgrades_uses_attribute_768()
    {
        // AllowSameVersionUpgrades flips the OLDERVERSIONFOUND attribute to
        // 256 | 512 (768) so the version range becomes inclusive on the upper
        // bound, allowing same-version reinstall.
        ResolvedPackage resolved = MakeResolved(
            upgrade: null,
            majorUpgrade: new MajorUpgradeModel { AllowSameVersionUpgrades = true });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(768, ((CellValue.IntValue)rows[0].Cells[4]).Value);
    }

    [Fact]
    public void Produce_major_upgrade_with_downgrade_allowed_skips_newer_version_row()
    {
        // Downgrade AllowDowngrades = true suppresses the NEWERVERSIONFOUND
        // row so an older install will not block. OLDERVERSIONFOUND row stays.
        ResolvedPackage resolved = MakeResolved(
            upgrade: null,
            majorUpgrade: new MajorUpgradeModel(),
            downgrade: new DowngradeModel { AllowDowngrades = true });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("OLDERVERSIONFOUND", ((CellValue.StringValue)row.Cells[6]).Value);
    }

    [Fact]
    public void Produce_skips_major_upgrade_emission_when_upgrade_table_is_set()
    {
        // Defense in depth: if both Upgrade and MajorUpgrade are configured,
        // the legacy emitter skips MajorUpgrade entirely and lets Upgrade win.
        // The recipe producer must reproduce that behaviour so the resulting
        // table is identical to the legacy MSI when validators allow the
        // (mis-)configuration through.
        UpgradeModel upgrade = new() { AllowDowngrades = false };
        ResolvedPackage resolved = MakeResolved(
            upgrade: upgrade,
            majorUpgrade: new MajorUpgradeModel(),
            version: new Version(1, 2, 3));

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // Only the two Upgrade rows should be emitted; MajorUpgrade is skipped.
        Assert.Equal(2, rows.Length);
        Assert.Equal("OLDERVERSIONFOUND", ((CellValue.StringValue)rows[0].Cells[6]).Value);
        Assert.Equal("NEWERVERSIONFOUND", ((CellValue.StringValue)rows[1].Cells[6]).Value);
        Assert.Equal(256, ((CellValue.IntValue)rows[0].Cells[4]).Value);
        Assert.Equal(258, ((CellValue.IntValue)rows[1].Cells[4]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        UpgradeTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        UpgradeModel? upgrade,
        MajorUpgradeModel? majorUpgrade,
        DowngradeModel? downgrade = null,
        Guid? upgradeCode = null,
        Version? version = null)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = version ?? new Version(1, 0, 0),
                UpgradeCode = upgradeCode ?? Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Upgrade = upgrade,
                MajorUpgrade = majorUpgrade,
                Downgrade = downgrade,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
