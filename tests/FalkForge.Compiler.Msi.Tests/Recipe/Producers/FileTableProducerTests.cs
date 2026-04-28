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

public sealed class FileTableProducerTests
{
    [Fact]
    public void Schema_has_eight_columns_file_pk_component_fk()
    {
        FileTableProducer producer = new();

        Assert.Equal("File", producer.Schema.Name.Value);
        Assert.Equal(8, producer.Schema.Columns.Length);
        Assert.Equal("File", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[1].Name);
        Assert.Equal("FileName", producer.Schema.Columns[2].Name);
        Assert.Equal("FileSize", producer.Schema.Columns[3].Name);
        Assert.Equal("Version", producer.Schema.Columns[4].Name);
        Assert.Equal("Language", producer.Schema.Columns[5].Name);
        Assert.Equal("Attributes", producer.Schema.Columns[6].Name);
        Assert.Equal("Sequence", producer.Schema.Columns[7].Name);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(1, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Produce_with_two_files_assigns_sequence_one_two()
    {
        ResolvedFile fileA = new()
        {
            FileId = "FileA",
            ComponentId = "MainComponent",
            FileName = "a.exe",
            FileSize = 1024,
            SourcePath = "a.exe",
            TargetDirectory = KnownFolder.ProgramFiles / "App",
        };
        ResolvedFile fileB = new()
        {
            FileId = "FileB",
            ComponentId = "MainComponent",
            FileName = "b.dll",
            FileSize = 2048,
            SourcePath = "b.dll",
            TargetDirectory = KnownFolder.ProgramFiles / "App",
        };
        ResolvedPackage resolved = MakeResolved(new[] { fileA, fileB });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal(1, ((CellValue.IntValue)rows[0].Cells[7]).Value);
        Assert.Equal(2, ((CellValue.IntValue)rows[1].Cells[7]).Value);
        CellValue.ForeignKey componentFk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[1]);
        Assert.Equal("Component", componentFk.TargetTable.Value);
        Assert.Equal("MainComponent", componentFk.TargetKey);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        FileTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<ResolvedFile> files)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
            },
            Components = new List<ResolvedComponent>(),
            Files = files,
        };
    }
}
