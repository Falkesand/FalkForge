using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class ComponentTableProducerTests
{
    [Fact]
    public void Schema_has_six_columns_component_pk_directory_fk()
    {
        ComponentTableProducer producer = new();

        Assert.Equal("Component", producer.Schema.Name.Value);
        Assert.Equal(6, producer.Schema.Columns.Length);
        Assert.Equal("Component", producer.Schema.Columns[0].Name);
        Assert.Equal("ComponentId", producer.Schema.Columns[1].Name);
        Assert.Equal("Directory_", producer.Schema.Columns[2].Name);
        Assert.Equal("Attributes", producer.Schema.Columns[3].Name);
        Assert.Equal("Condition", producer.Schema.Columns[4].Name);
        Assert.Equal("KeyPath", producer.Schema.Columns[5].Name);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(2, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Directory", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Produce_with_one_component_emits_one_row_matching_resolved_shape()
    {
        // No install directory configured → component leaf id falls through to
        // the deterministic D_<segment>_<hash> synthesis. The Directory_ FK
        // must point at that synthesized id, not at the bare KnownFolder root
        // token, so it lines up with the rows DirectoryTableProducer emits.
        Guid componentGuid = new("11111111-2222-3333-4444-555555555555");
        ResolvedComponent component = new()
        {
            Id = "MainComponent",
            Guid = componentGuid,
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = "MainExe",
            Files = new List<ResolvedFile>(),
            Condition = null,
            NeverOverwrite = false,
            Permanent = false,
        };
        ResolvedPackage resolved = MakeResolved(new[] { component }, ProcessorArchitecture.X64);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        Assert.Equal("MainComponent", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal(
            componentGuid.ToString("B").ToUpperInvariant(),
            ((CellValue.StringValue)rows[0].Cells[1]).Value);
        CellValue.ForeignKey dirFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[2]);
        Assert.Equal("Directory", dirFk.TargetTable.Value);
        // No install dir → leaf id = D_App_<hash(ProgramFilesFolder)>.
        Assert.StartsWith("D_App_", dirFk.TargetKey);
        // 256 = 64-bit; no NeverOverwrite or Permanent bits.
        Assert.Equal(256, ((CellValue.IntValue)rows[0].Cells[3]).Value);
        Assert.Equal(string.Empty, ((CellValue.StringValue)rows[0].Cells[4]).Value);
        Assert.Equal("MainExe", ((CellValue.StringValue)rows[0].Cells[5]).Value);
    }

    [Fact]
    public void Component_at_install_directory_leaf_uses_INSTALLDIR_id()
    {
        // When the component directory equals the package's configured install
        // directory, the leaf id collapses to the canonical "INSTALLDIR"
        // identifier so MSI Formatted strings like "[INSTALLDIR]bin\tool.exe"
        // resolve correctly.
        InstallPath installDir = KnownFolder.ProgramFiles / "App";
        ResolvedComponent component = new()
        {
            Id = "C1",
            Guid = Guid.NewGuid(),
            Directory = installDir,
            KeyPath = "key1",
            Files = new List<ResolvedFile>(),
        };
        ResolvedPackage resolved = MakeResolved(
            new[] { component },
            ProcessorArchitecture.X64,
            installDir);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey dirFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[2]);
        Assert.Equal("INSTALLDIR", dirFk.TargetKey);
    }

    [Fact]
    public void Component_under_known_folder_root_uses_root_token_id()
    {
        // Components whose directory has zero segments below the KnownFolder
        // root collapse to the root token id (e.g. ProgramFilesFolder),
        // matching the Directory row the producer emits at depth zero.
        InstallPath rootOnly = KnownFolder.ProgramFiles / "";
        ResolvedComponent component = new()
        {
            Id = "C1",
            Guid = Guid.NewGuid(),
            Directory = rootOnly,
            KeyPath = "key1",
            Files = new List<ResolvedFile>(),
        };
        ResolvedPackage resolved = MakeResolved(new[] { component }, ProcessorArchitecture.X64);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey dirFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[2]);
        Assert.Equal("ProgramFilesFolder", dirFk.TargetKey);
    }

    [Fact]
    public void Component_below_install_directory_uses_D_segment_hash_id()
    {
        // For paths nested under the install dir leaf, the synthesizer hashes
        // each subdirectory off "INSTALLDIR" so component FKs resolve to
        // intermediate D_* directory rows DirectoryTableProducer emits.
        InstallPath installDir = KnownFolder.ProgramFiles / "App";
        ResolvedComponent component = new()
        {
            Id = "C1",
            Guid = Guid.NewGuid(),
            Directory = installDir / "bin",
            KeyPath = "key1",
            Files = new List<ResolvedFile>(),
        };
        ResolvedPackage resolved = MakeResolved(
            new[] { component },
            ProcessorArchitecture.X64,
            installDir);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey dirFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[2]);
        Assert.StartsWith("D_bin_", dirFk.TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        ComponentTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<ResolvedComponent> components,
        ProcessorArchitecture architecture,
        InstallPath? installDir = null)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Architecture = architecture,
                DefaultInstallDirectory = installDir,
            },
            Components = components,
            Files = new List<ResolvedFile>(),
        };
    }
}
