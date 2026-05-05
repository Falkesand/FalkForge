using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Tests for <see cref="RecipeContentHasher"/>. The hasher must produce a
/// stable 32-byte SHA-256 digest over the canonical form of an
/// <see cref="MsiDatabaseRecipe"/>. <see cref="MsiDatabaseRecipe.ContentHash"/>
/// itself is intentionally excluded from the hash payload to avoid the
/// chicken-and-egg problem of hashing a field that contains the hash.
/// </summary>
public sealed class RecipeContentHasherTests
{
    [Fact]
    public void Compute_with_minimal_recipe_returns_32_bytes()
    {
        MsiDatabaseRecipe recipe = MakeMinimalRecipe();

        ReadOnlyMemory<byte> hash = RecipeContentHasher.Compute(recipe);

        Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void Compute_same_recipe_twice_returns_equal_hash()
    {
        MsiDatabaseRecipe recipe = MakeMinimalRecipe();

        ReadOnlyMemory<byte> hash1 = RecipeContentHasher.Compute(recipe);
        ReadOnlyMemory<byte> hash2 = RecipeContentHasher.Compute(recipe);

        Assert.True(hash1.Span.SequenceEqual(hash2.Span));
    }

    [Fact]
    public void Compute_recipes_differing_by_one_property_value_return_different_hashes()
    {
        MsiDatabaseRecipe a = MakeRecipeWithSinglePropertyRow("ProductCode", "{A}");
        MsiDatabaseRecipe b = MakeRecipeWithSinglePropertyRow("ProductCode", "{B}");

        ReadOnlyMemory<byte> hashA = RecipeContentHasher.Compute(a);
        ReadOnlyMemory<byte> hashB = RecipeContentHasher.Compute(b);

        Assert.False(hashA.Span.SequenceEqual(hashB.Span));
    }

    [Fact]
    public void Compute_recipes_differing_by_table_order_return_different_hashes()
    {
        // Topological order is part of the canonical form. Swapping two
        // tables must change the hash even when their content is identical.
        RecipeTable t1 = MakeEmptyTable("AlphaTable");
        RecipeTable t2 = MakeEmptyTable("BetaTable");

        MsiDatabaseRecipe ab = MakeRecipeWithTables(ImmutableArray.Create(t1, t2));
        MsiDatabaseRecipe ba = MakeRecipeWithTables(ImmutableArray.Create(t2, t1));

        ReadOnlyMemory<byte> hashAb = RecipeContentHasher.Compute(ab);
        ReadOnlyMemory<byte> hashBa = RecipeContentHasher.Compute(ba);

        Assert.False(hashAb.Span.SequenceEqual(hashBa.Span));
    }

    [Fact]
    public void Compute_recipes_with_streams_in_different_insertion_order_return_equal_hash()
    {
        // Stream dictionary insertion order is implementation-defined and
        // therefore must not affect the hash. The hasher sorts stream keys
        // ordinally before incorporating them.
        StreamSource s1 = new StreamSource.InMemory(
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            new ReadOnlyMemory<byte>(new byte[32]));
        StreamSource s2 = new StreamSource.InMemory(
            new ReadOnlyMemory<byte>(new byte[] { 4, 5, 6 }),
            new ReadOnlyMemory<byte>(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 }));

        ImmutableDictionary<string, StreamSource> ab = ImmutableDictionary<string, StreamSource>.Empty
            .Add("alpha", s1)
            .Add("beta", s2);

        ImmutableDictionary<string, StreamSource> ba = ImmutableDictionary<string, StreamSource>.Empty
            .Add("beta", s2)
            .Add("alpha", s1);

        MsiDatabaseRecipe r1 = MakeMinimalRecipe() with { Streams = ab };
        MsiDatabaseRecipe r2 = MakeMinimalRecipe() with { Streams = ba };

        ReadOnlyMemory<byte> hash1 = RecipeContentHasher.Compute(r1);
        ReadOnlyMemory<byte> hash2 = RecipeContentHasher.Compute(r2);

        Assert.True(hash1.Span.SequenceEqual(hash2.Span));
    }

    [Fact]
    public void Compute_recipe_with_empty_streams_succeeds()
    {
        MsiDatabaseRecipe recipe = MakeMinimalRecipe();

        ReadOnlyMemory<byte> hash = RecipeContentHasher.Compute(recipe);

        Assert.Equal(32, hash.Length);
    }

    private static MsiDatabaseRecipe MakeMinimalRecipe()
    {
        return new MsiDatabaseRecipe
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
            CabinetEmbeddings = ImmutableArray<CabinetEmbedding>.Empty,
            ContentHash = ReadOnlyMemory<byte>.Empty,
        };
    }

    private static MsiDatabaseRecipe MakeRecipeWithSinglePropertyRow(string name, string value)
    {
        RecipeColumn nameColumn = new()
        {
            Name = "Property",
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false,
        };
        RecipeColumn valueColumn = new()
        {
            Name = "Value",
            Type = ColumnType.String,
            Width = 0,
            Nullable = false,
            LocalizableKey = false,
        };

        RecipeRow row = new()
        {
            Cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(name),
                new CellValue.StringValue(value)),
        };

        RecipeTable table = new()
        {
            Name = TableId.Create("Property").Value,
            Columns = ImmutableArray.Create(nameColumn, valueColumn),
            Rows = ImmutableArray.Create(row),
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql = "CREATE TABLE `Property` (...)",
            InsertViewSql = "SELECT `Property`, `Value` FROM `Property`",
        };

        return MakeMinimalRecipe() with { Tables = ImmutableArray.Create(table) };
    }

    private static RecipeTable MakeEmptyTable(string name)
    {
        RecipeColumn col = new()
        {
            Name = "Id",
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false,
        };

        return new RecipeTable
        {
            Name = TableId.Create(name).Value,
            Columns = ImmutableArray.Create(col),
            Rows = ImmutableArray<RecipeRow>.Empty,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            CreateTableSql = $"CREATE TABLE `{name}` (`Id` CHAR(72) NOT NULL PRIMARY KEY `Id`)",
            InsertViewSql = $"SELECT `Id` FROM `{name}`",
        };
    }

    private static MsiDatabaseRecipe MakeRecipeWithTables(ImmutableArray<RecipeTable> tables)
    {
        return MakeMinimalRecipe() with { Tables = tables };
    }
}
