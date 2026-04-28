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
            Template = ";1033",
            Keywords = "K",
            Comments = "C",
            RevisionNumber = 200,
            CodePage = 1252
        };

        ReadOnlyMemory<byte> hash = SHA256.HashData(ReadOnlySpan<byte>.Empty);

        MsiDatabaseRecipe recipe = new()
        {
            Tables = ImmutableArray.Create(table),
            SummaryInfo = summary,
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbedding = null,
            ContentHash = hash
        };

        Assert.Single(recipe.Tables);
        Assert.Equal(summary, recipe.SummaryInfo);
        Assert.Empty(recipe.Streams);
        Assert.True(recipe.FileSequencing.IsEmpty);
        Assert.Null(recipe.CabinetEmbedding);
        Assert.Equal(32, recipe.ContentHash.Length);
    }

    [Fact]
    public void Construct_with_cabinet_embedding_preserves_value()
    {
        byte[] payload = [0xAA, 0xBB];
        StreamSource cabSource = new StreamSource.InMemory(payload, SHA256.HashData(payload));
        CabinetEmbedding embedding = new("#Cab1.cab", cabSource);

        MsiDatabaseRecipe recipe = new()
        {
            Tables = ImmutableArray<RecipeTable>.Empty,
            SummaryInfo = new SummaryInfoRecipe
            {
                Title = "",
                Subject = "",
                Author = "",
                Template = "",
                Keywords = "",
                Comments = "",
                RevisionNumber = 0,
                CodePage = 1252
            },
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbedding = embedding,
            ContentHash = ReadOnlyMemory<byte>.Empty
        };

        Assert.Equal(embedding, recipe.CabinetEmbedding);
    }
}
