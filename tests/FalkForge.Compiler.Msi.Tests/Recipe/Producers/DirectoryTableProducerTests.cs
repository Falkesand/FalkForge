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
    public void Produce_with_root_only_emits_one_row_with_null_parent()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new DirectoryModel { Id = "TARGETDIR", Name = "SourceDir", ParentId = null },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Single(rows);
        Assert.Equal("TARGETDIR", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.IsType<CellValue.Null>(rows[0].Cells[1]);
        Assert.Equal("SourceDir", ((CellValue.StringValue)rows[0].Cells[2]).Value);
    }

    [Fact]
    public void Produce_with_nested_directory_emits_foreign_key_cell_to_parent()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new DirectoryModel { Id = "TARGETDIR", Name = "SourceDir", ParentId = null },
            new DirectoryModel { Id = "INSTALLDIR", Name = "AppFolder", ParentId = "TARGETDIR" },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[1].Cells[1]);
        Assert.Equal("Directory", fk.TargetTable.Value);
        Assert.Equal("TARGETDIR", fk.TargetKey);
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

    private static ResolvedPackage MakeResolved(IReadOnlyList<DirectoryModel> directories)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new System.Version(1, 0, 0),
                Directories = directories,
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
