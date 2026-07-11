using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class MsiLockPermissionsExTableProducerTests
{
    [Fact]
    public void Schema_has_five_columns_msilockpermissionsex_pk_no_foreign_keys()
    {
        MsiLockPermissionsExTableProducer producer = new();

        Assert.Equal("MsiLockPermissionsEx", producer.Schema.Name.Value);
        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("MsiLockPermissionsEx", producer.Schema.Columns[0].Name);
        Assert.Equal("LockObject", producer.Schema.Columns[1].Name);
        Assert.Equal("Table", producer.Schema.Columns[2].Name);
        Assert.Equal("SDDLText", producer.Schema.Columns[3].Name);
        Assert.Equal("Condition", producer.Schema.Columns[4].Name);

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);

        // The DDL declares no foreign keys — the LockObject column points at
        // File / Registry / CreateFolder rows but MSI keeps that link
        // implicit because the parent table is selected by the Table column.
        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateMsiLockPermissionsExTable: each non-null
        // column is plain CHAR (no LOCALIZABLE flag); MsiLockPermissionsEx
        // and SDDLText are non-null, Condition is nullable, widths follow
        // the DDL exactly.
        MsiLockPermissionsExTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);
        Assert.Equal(ColumnType.String, columns[3].Type);
        Assert.Equal(ColumnType.String, columns[4].Type);

        Assert.Equal(72, columns[0].Width);
        Assert.Equal(72, columns[1].Width);
        Assert.Equal(32, columns[2].Width);
        Assert.Equal(255, columns[3].Width);
        Assert.Equal(255, columns[4].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.False(columns[2].Nullable);
        Assert.False(columns[3].Nullable);
        Assert.True(columns[4].Nullable);
    }

    [Fact]
    public void Produce_with_no_permissions_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<PermissionModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_sddl_permission_with_correct_cells()
    {
        // Mirrors the MsiLockPermissionsEx branch of the legacy
        // EmitPermissions: when Sddl is non-empty the producer emits a
        // (PRM_NNNN, LockObject, Table, Sddl, Condition=null) row. The
        // synthesized PRM_NNNN id matches the legacy $"PRM_{index:D4}".
        // Condition is null in the legacy emitter and the producer does
        // not source it from the model.
        PermissionModel perm = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            Sddl = "O:BAG:BAD:(A;;FA;;;BA)",
        };
        ResolvedPackage resolved = MakeResolved(new[] { perm });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("PRM_0000", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("INSTALLDIR", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal("CreateFolder", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("O:BAG:BAD:(A;;FA;;;BA)", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.IsType<CellValue.Null>(row.Cells[4]);
    }

    [Fact]
    public void Produce_skips_entries_whose_sddl_is_null_or_empty()
    {
        // User-driven entries flow through LockPermissionsTableProducer; the
        // MsiLockPermissionsEx producer must skip them so the two table
        // producers partition the input cleanly.
        PermissionModel userOnly = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            Sddl = null,
            User = "Everyone",
            Permission = 1,
        };
        PermissionModel emptySddl = new()
        {
            LockObject = "INSTALLDIR",
            Table = "Registry",
            Sddl = string.Empty,
            User = "Admin",
        };
        ResolvedPackage resolved = MakeResolved(new[] { userOnly, emptySddl });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_synthesises_prm_ids_using_input_index_across_skipped_entries()
    {
        // The legacy emitter walks the Permissions list with a single index
        // counter and synthesises 'PRM_{index:D4}' from the *input position*
        // — User-only entries do not consume an id but they still advance
        // the counter for the next Sddl entry. A producer that re-numbered
        // contiguously would clash with a recipe authored under the legacy
        // emitter; preserve the input-index synthesis exactly.
        PermissionModel sddlA = new()
        {
            LockObject = "Folder1",
            Table = "CreateFolder",
            Sddl = "O:BA",
        };
        PermissionModel userBetween = new()
        {
            LockObject = "Folder2",
            Table = "CreateFolder",
            User = "Everyone",
        };
        PermissionModel sddlB = new()
        {
            LockObject = "Folder3",
            Table = "CreateFolder",
            Sddl = "O:SY",
        };
        ResolvedPackage resolved = MakeResolved(new[] { sddlA, userBetween, sddlB });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("PRM_0000", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("Folder1", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal("PRM_0002", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("Folder3", ((CellValue.StringValue)rows[1].Cells[1]).Value);
    }

    [Fact]
    public void Produce_emits_null_condition_cell_for_every_row()
    {
        // The legacy emitter passes null for the Condition column on every
        // row; PermissionModel has no field for it. Pin the literal so a
        // future field addition does not silently change MSI behaviour.
        PermissionModel a = new() { LockObject = "L1", Table = "CreateFolder", Sddl = "O:BA" };
        PermissionModel b = new() { LockObject = "L2", Table = "CreateFolder", Sddl = "O:SY" };
        ResolvedPackage resolved = MakeResolved(new[] { a, b });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.IsType<CellValue.Null>(rows[0].Cells[4]);
        Assert.IsType<CellValue.Null>(rows[1].Cells[4]);
    }

    [Fact]
    public void Produce_emits_row_for_service_sddl_permission_using_serviceinstall_id_not_raw_service_name()
    {
        // C4 (SDDL half): mirrors LockPermissionsTableProducerTests' User-driven
        // case for the SDDL-driven path. ServiceBuilder.Permission(...) stamps
        // the model's LockObject with the raw service name; the producer must
        // recompute the effective LockObject as the ServiceInstall row's own
        // synthesized primary key.
        ServiceModel service = new()
        {
            Name = "MyService",
            DisplayName = "My Service",
            Executable = "svc.exe",
            Permissions = new[]
            {
                new PermissionModel
                {
                    LockObject = "MyService",
                    Table = "ServiceInstall",
                    Sddl = "D:(A;;RPWP;;;WD)",
                },
            },
        };
        ResolvedPackage resolved = MakeResolved(Array.Empty<PermissionModel>(), new[] { service });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("PRM_0000", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("SVC_MyService", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal("ServiceInstall", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("D:(A;;RPWP;;;WD)", ((CellValue.StringValue)row.Cells[3]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        MsiLockPermissionsExTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<PermissionModel> permissions,
        IReadOnlyList<ServiceModel>? services = null)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Permissions = permissions,
                Services = services ?? Array.Empty<ServiceModel>(),
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
