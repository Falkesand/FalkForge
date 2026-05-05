using System.Collections.Immutable;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class MsiDatabaseRecipeTests
{
    [Fact]
    public void Construct_with_all_required_members_succeeds()
    {
        TableId tableName = TableId.Create("Property").Value;
        RecipeTable table = new()
        {
            Name = tableName,
            Columns = ImmutableArray.Create(
                new RecipeColumn { Name = "Property", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
                new RecipeColumn { Name = "Value", Type = ColumnType.String, Width = 0, Nullable = true, LocalizableKey = false }),
            Rows = ImmutableArray<RecipeRow>.Empty,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql = "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR PRIMARY KEY `Property`)",
            InsertViewSql = "SELECT `Property`, `Value` FROM `Property`"
        };

        SummaryInfoRecipe summary = new()
        {
            Title = "T",
            Subject = "S",
            Author = "A",
            Template = "x64;1033",
            Keywords = "K",
            Comments = "C",
            RevisionNumber = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            CodePage = 1252,
            CreatingApplication = "FalkForge",
            WordCount = 2,
            PageCount = 200,
            Security = 2,
        };

        ReadOnlyMemory<byte> hash = SHA256.HashData(ReadOnlySpan<byte>.Empty);

        MsiDatabaseRecipe recipe = new()
        {
            Tables = ImmutableArray.Create(table),
            SummaryInfo = summary,
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbeddings = ImmutableArray<CabinetEmbedding>.Empty,
            ContentHash = hash
        };

        Assert.Single(recipe.Tables);
        Assert.Equal(summary, recipe.SummaryInfo);
        Assert.Empty(recipe.Streams);
        Assert.True(recipe.FileSequencing.IsEmpty);
        Assert.True(recipe.CabinetEmbeddings.IsEmpty);
        Assert.Equal(32, recipe.ContentHash.Length);
    }

    [Fact]
    public void Construct_with_cabinet_embeddings_preserves_values()
    {
        byte[] payload = [0xAA, 0xBB];
        StreamSource cabSource = new StreamSource.InMemory(payload, SHA256.HashData(payload));
        CabinetEmbedding embedding = new("#Data.cab", cabSource);

        MsiDatabaseRecipe recipe = new()
        {
            Tables = ImmutableArray<RecipeTable>.Empty,
            SummaryInfo = new SummaryInfoRecipe
            {
                Title = string.Empty,
                Subject = string.Empty,
                Author = string.Empty,
                Template = string.Empty,
                Keywords = string.Empty,
                Comments = string.Empty,
                RevisionNumber = string.Empty,
                CodePage = 1252,
                CreatingApplication = string.Empty,
                WordCount = 0,
                PageCount = 0,
                Security = 0,
            },
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbeddings = ImmutableArray.Create(embedding),
            ContentHash = ReadOnlyMemory<byte>.Empty
        };

        Assert.Single(recipe.CabinetEmbeddings);
        Assert.Equal(embedding, recipe.CabinetEmbeddings[0]);
    }
}
