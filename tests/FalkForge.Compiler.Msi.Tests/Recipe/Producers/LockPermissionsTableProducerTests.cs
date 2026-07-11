using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class LockPermissionsTableProducerTests
{
    [Fact]
    public void Schema_has_five_columns_composite_pk_no_foreign_keys()
    {
        LockPermissionsTableProducer producer = new();

        Assert.Equal("LockPermissions", producer.Schema.Name.Value);
        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("LockObject", producer.Schema.Columns[0].Name);
        Assert.Equal("Table", producer.Schema.Columns[1].Name);
        Assert.Equal("Domain", producer.Schema.Columns[2].Name);
        Assert.Equal("User", producer.Schema.Columns[3].Name);
        Assert.Equal("Permission", producer.Schema.Columns[4].Name);

        // Composite PK matches MsiTableDefinitions.CreateLockPermissionsTable:
        // PRIMARY KEY (LockObject, Table, Domain, User).
        Assert.Equal(4, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
        Assert.Equal(2, producer.Schema.PrimaryKey[2].Value);
        Assert.Equal(3, producer.Schema.PrimaryKey[3].Value);

        // The DDL declares no foreign keys even though LockObject can reference
        // File / Registry / CreateFolder rows — MSI keeps that link implicit
        // because the parent table is selected by the Table column at install
        // time and the schema cannot express a polymorphic FK.
        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateLockPermissionsTable: LockObject CHAR(72)
        // NN, Table CHAR(32) NN, Domain CHAR(255) (nullable), User CHAR(255)
        // NN, Permission LONG (nullable).
        LockPermissionsTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.String, columns[2].Type);
        Assert.Equal(ColumnType.String, columns[3].Type);
        Assert.Equal(ColumnType.Integer, columns[4].Type);

        Assert.Equal(72, columns[0].Width);
        Assert.Equal(32, columns[1].Width);
        Assert.Equal(255, columns[2].Width);
        Assert.Equal(255, columns[3].Width);
        Assert.Equal(4, columns[4].Width);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
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
    public void Produce_emits_one_row_per_user_permission_with_correct_cells()
    {
        // Mirrors the LockPermissions branch of the legacy EmitPermissions:
        // when Sddl is null/empty and User is non-empty the producer emits a
        // (LockObject, Table, Domain, User, Permission) row. Sddl-driven
        // entries flow through a separate MsiLockPermissionsEx producer and
        // are out of scope here.
        PermissionModel perm = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            Domain = "ACME",
            User = "Admins",
            Permission = 0x1F01FF,
        };
        ResolvedPackage resolved = MakeResolved(new[] { perm });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("INSTALLDIR", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("CreateFolder", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal("ACME", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("Admins", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal(0x1F01FF, ((CellValue.IntValue)row.Cells[4]).Value);
    }

    [Fact]
    public void Produce_emits_empty_string_cell_when_domain_is_null()
    {
        // Domain is part of the composite primary key (LockObject, Table, Domain, User).
        // PK columns cannot be null — the recipe PrimaryKeyValidator rejects null PK cells.
        // To match legacy TableEmitter behaviour (SetString with null → empty string in msi.dll),
        // the producer emits CellValue.StringValue("") rather than CellValue.Null when
        // PermissionModel.Domain is null.
        PermissionModel perm = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            Domain = null,
            User = "Everyone",
            Permission = 0,
        };
        ResolvedPackage resolved = MakeResolved(new[] { perm });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue domainCell = rows[0].Cells[2];
        Assert.IsType<CellValue.StringValue>(domainCell);
        Assert.Equal(string.Empty, ((CellValue.StringValue)domainCell).Value);
    }

    [Fact]
    public void Produce_skips_entries_whose_sddl_is_set()
    {
        // Sddl-driven permissions belong to MsiLockPermissionsEx; the
        // LockPermissions producer must skip them so the two table producers
        // partition the input list cleanly.
        PermissionModel sddlOnly = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            Sddl = "O:BAG:BAD:(A;;FA;;;BA)",
            Domain = null,
            User = null,
        };
        ResolvedPackage resolved = MakeResolved(new[] { sddlOnly });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_skips_entries_whose_user_is_null_or_empty()
    {
        // Without Sddl and without User there is nothing to emit on either
        // path — mirror the legacy guard which only writes a LockPermissions
        // row when User has a value.
        PermissionModel nullUser = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            User = null,
        };
        PermissionModel emptyUser = new()
        {
            LockObject = "INSTALLDIR",
            Table = "Registry",
            User = string.Empty,
        };
        ResolvedPackage resolved = MakeResolved(new[] { nullUser, emptyUser });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_only_emits_user_permissions_when_input_mixes_sddl_and_user_entries()
    {
        PermissionModel sddl = new()
        {
            LockObject = "Folder1",
            Table = "CreateFolder",
            Sddl = "O:BAG:BAD:(A;;FA;;;BA)",
        };
        PermissionModel user = new()
        {
            LockObject = "Folder2",
            Table = "CreateFolder",
            User = "Everyone",
            Permission = 1,
        };
        ResolvedPackage resolved = MakeResolved(new[] { sddl, user });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("Folder2", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("Everyone", ((CellValue.StringValue)row.Cells[3]).Value);
    }

    [Fact]
    public void Produce_emits_row_for_service_permission_using_serviceinstall_id_not_raw_service_name()
    {
        // C4: ServiceBuilder.Permission(...) collects entries onto
        // ServiceModel.Permissions, but only PackageModel.Permissions fed this
        // producer, so a permission authored on a service was silently dropped.
        // ServiceBuilder stamps PermissionModel.LockObject with the raw service
        // name (see ServiceBuilderPermissionTests) — the producer must recompute
        // the effective LockObject as the synthesized ServiceInstall primary key
        // ("SVC_" + sanitized name), which is what the ServiceInstall table
        // actually uses as its row key, not the plain service name.
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
                    Domain = "BUILTIN",
                    User = "Administrators",
                    Permission = 0x000F01FF,
                },
            },
        };
        ResolvedPackage resolved = MakeResolved(Array.Empty<PermissionModel>(), new[] { service });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("SVC_MyService", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("ServiceInstall", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal("BUILTIN", ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal("Administrators", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal(0x000F01FF, ((CellValue.IntValue)row.Cells[4]).Value);
    }

    [Fact]
    public void Produce_emits_both_package_level_and_service_level_permissions()
    {
        PermissionModel packageLevel = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            User = "Everyone",
            Permission = 1,
        };
        ServiceModel service = new()
        {
            Name = "Svc",
            DisplayName = "Svc",
            Executable = "svc.exe",
            Permissions = new[]
            {
                new PermissionModel { LockObject = "Svc", Table = "ServiceInstall", User = "Users", Permission = 2 },
            },
        };
        ResolvedPackage resolved = MakeResolved(new[] { packageLevel }, new[] { service });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("INSTALLDIR", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("SVC_Svc", ((CellValue.StringValue)rows[1].Cells[0]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        LockPermissionsTableProducer producer = new();
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
