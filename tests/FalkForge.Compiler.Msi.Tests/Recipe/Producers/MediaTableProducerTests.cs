using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class MediaTableProducerTests
{
    [Fact]
    public void Schema_has_six_columns_diskid_pk_no_fks()
    {
        MediaTableProducer producer = new();

        Assert.Equal("Media", producer.Schema.Name.Value);
        Assert.Equal(6, producer.Schema.Columns.Length);
        Assert.Equal("DiskId", producer.Schema.Columns[0].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Empty(producer.Schema.ForeignKeys);
    }

    [Fact]
    public void Produce_with_empty_files_emits_one_row_with_lastsequence_one()
    {
        MediaTableProducer producer = new();
        RecipeBuildContext ctx = MakeContext(filesCount: 0);

        Result<ImmutableArray<RecipeRow>> result = producer.Produce(ctx);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        CellValue.IntValue diskId = Assert.IsType<CellValue.IntValue>(result.Value[0].Cells[0]);
        Assert.Equal(1, diskId.Value);
        CellValue.IntValue lastSeq = Assert.IsType<CellValue.IntValue>(result.Value[0].Cells[1]);
        Assert.Equal(1, lastSeq.Value);
    }

    [Fact]
    public void Produce_with_three_files_emits_lastsequence_three()
    {
        MediaTableProducer producer = new();
        RecipeBuildContext ctx = MakeContext(filesCount: 3);

        Result<ImmutableArray<RecipeRow>> result = producer.Produce(ctx);

        Assert.True(result.IsSuccess);
        CellValue.IntValue lastSeq = Assert.IsType<CellValue.IntValue>(result.Value[0].Cells[1]);
        Assert.Equal(3, lastSeq.Value);
    }

    private static RecipeBuildContext MakeContext(int filesCount)
    {
        List<ResolvedFile> files = new();
        for (int i = 0; i < filesCount; i++)
        {
            files.Add(new ResolvedFile
            {
                SourcePath = $"f{i}.txt",
                TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                FileName = $"f{i}.txt",
                FileSize = 1L,
                ComponentId = "C1",
                FileId = $"F{i}",
            });
        }

        ResolvedPackage resolved = new()
        {
            Package = new PackageModel { Name = "T", Manufacturer = "M", Version = new System.Version(1, 0, 0) },
            Components = new List<ResolvedComponent>(),
            Files = files,
        };

        return new RecipeBuildContext(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
    }
}
