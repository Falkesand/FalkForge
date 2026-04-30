using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class DirectoryTableProducerTests
{
    [Fact]
    public void Schema_has_three_columns_directory_pk_parent_fk()
    {
        DirectoryTableProducer producer = new();

        Assert.Equal("Directory", producer.Schema.Name.Value);
        Assert.Equal(3, producer.Schema.Columns.Length);
        Assert.Equal("Directory", producer.Schema.Columns[0].Name);
        Assert.Equal("Directory_Parent", producer.Schema.Columns[1].Name);
        Assert.Equal("DefaultDir", producer.Schema.Columns[2].Name);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(1, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Directory", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Empty_package_emits_only_targetdir_row()
    {
        // No install directory, no components, no files → the single mandatory
        // TARGETDIR row is the only output. TargetDir has no parent.
        ResolvedPackage resolved = MakeResolved(installDir: null);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        Assert.Equal("TARGETDIR", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.IsType<CellValue.Null>(rows[0].Cells[1]);
        Assert.Equal("SourceDir", ((CellValue.StringValue)rows[0].Cells[2]).Value);
    }

    [Fact]
    public void Install_directory_chain_synthesizes_targetdir_root_and_installdir_leaf()
    {
        // ProgramFiles / "App" with one component at the same path → expect
        // three rows, in FK-safe order: TARGETDIR, ProgramFilesFolder, INSTALLDIR.
        InstallPath installDir = KnownFolder.ProgramFiles / "App";
        ResolvedComponent component = new()
        {
            Id = "C1",
            Guid = System.Guid.NewGuid(),
            Directory = installDir,
            KeyPath = "F1",
            Files = new List<ResolvedFile>(),
        };
        ResolvedPackage resolved = MakeResolved(installDir, components: new[] { component });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
        AssertRow(rows[0], id: "TARGETDIR", parent: null, name: "SourceDir");
        AssertRow(rows[1], id: "ProgramFilesFolder", parent: "TARGETDIR", name: ".");
        AssertRow(rows[2], id: "INSTALLDIR", parent: "ProgramFilesFolder", name: "App");
    }

    [Fact]
    public void Two_components_in_same_directory_emit_no_duplicate_rows()
    {
        // Two components both under ProgramFiles / "App" → the directory row
        // for INSTALLDIR is emitted once, never twice, even though both
        // components reference the same target.
        InstallPath installDir = KnownFolder.ProgramFiles / "App";
        ResolvedComponent c1 = MakeComponent("C1", installDir);
        ResolvedComponent c2 = MakeComponent("C2", installDir);
        ResolvedPackage resolved = MakeResolved(installDir, components: new[] { c1, c2 });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(3, rows.Length);
        Assert.Equal(rows.Length, CountUnique(rows));
    }

    [Fact]
    public void Components_under_different_known_folder_roots_emit_both_root_rows()
    {
        // Two components, one under ProgramFiles, one under CommonAppData.
        // Both root tokens must be materialized as Directory rows parented to
        // TARGETDIR so component FKs resolve.
        InstallPath installDir = KnownFolder.ProgramFiles / "App";
        InstallPath altDir = KnownFolder.CommonAppData / "AppData";
        ResolvedComponent c1 = MakeComponent("C1", installDir);
        ResolvedComponent c2 = MakeComponent("C2", altDir);
        ResolvedPackage resolved = MakeResolved(installDir, components: new[] { c1, c2 });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Contains(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "ProgramFilesFolder");
        Assert.Contains(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "CommonAppDataFolder");
    }

    [Fact]
    public void Parents_are_emitted_before_their_children_for_fk_safety()
    {
        // FK validators reject rows whose parent has not yet been emitted.
        // The synthesizer must produce a topological order: every
        // Directory_Parent foreign key must point at an ID already seen.
        InstallPath installDir = KnownFolder.ProgramFiles / "App";
        InstallPath deepDir = installDir / "bin" / "tools";
        ResolvedComponent component = MakeComponent("C1", deepDir);
        ResolvedPackage resolved = MakeResolved(installDir, components: new[] { component });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        HashSet<string> seen = new();
        foreach (RecipeRow row in rows)
        {
            string id = ((CellValue.StringValue)row.Cells[0]).Value;
            if (row.Cells[1] is CellValue.ForeignKey fk)
            {
                Assert.Contains(fk.TargetKey, seen);
            }

            seen.Add(id);
        }
    }

    private static int CountUnique(ImmutableArray<RecipeRow> rows)
    {
        HashSet<string> ids = new();
        foreach (RecipeRow row in rows)
        {
            ids.Add(((CellValue.StringValue)row.Cells[0]).Value);
        }

        return ids.Count;
    }

    private static void AssertRow(RecipeRow row, string id, string? parent, string name)
    {
        Assert.Equal(id, ((CellValue.StringValue)row.Cells[0]).Value);
        if (parent is null)
        {
            Assert.IsType<CellValue.Null>(row.Cells[1]);
        }
        else
        {
            CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[1]);
            Assert.Equal("Directory", fk.TargetTable.Value);
            Assert.Equal(parent, fk.TargetKey);
        }

        Assert.Equal(name, ((CellValue.StringValue)row.Cells[2]).Value);
    }

    private static ResolvedComponent MakeComponent(string id, InstallPath directory)
    {
        return new ResolvedComponent
        {
            Id = id,
            Guid = System.Guid.NewGuid(),
            Directory = directory,
            KeyPath = id + "_key",
            Files = new List<ResolvedFile>(),
        };
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        DirectoryTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        InstallPath? installDir,
        IReadOnlyList<ResolvedComponent>? components = null,
        IReadOnlyList<ResolvedFile>? files = null)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new System.Version(1, 0, 0),
                DefaultInstallDirectory = installDir,
            },
            Components = components ?? new List<ResolvedComponent>(),
            Files = files ?? new List<ResolvedFile>(),
        };
    }
}
