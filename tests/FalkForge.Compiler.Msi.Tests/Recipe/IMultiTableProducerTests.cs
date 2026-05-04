using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Verifies the <see cref="IMultiTableProducer"/> contract: a producer returning
/// multiple <see cref="RecipeTable"/> instances sees all of its tables appended
/// to the final <see cref="MsiDatabaseRecipe"/>. Tests are written against the
/// interface shape via a fake implementation, confirming the contract is
/// structurally sound before any real producer ships.
/// </summary>
public sealed class IMultiTableProducerTests
{
    // ── Interface shape ───────────────────────────────────────────────────────

    [Fact]
    public void IMultiTableProducer_can_be_implemented_and_returns_Result_of_RecipeTable_array()
    {
        // Arrange: a fake that returns two recipe tables with no rows.
        IMultiTableProducer producer = new FakeDoubleTableProducer();
        RecipeBuildContext context = MakeContext();

        // Act
        Result<ImmutableArray<RecipeTable>> result = producer.Produce(context);

        // Assert: well-formed result with exactly two tables.
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Length);
    }

    [Fact]
    public void IMultiTableProducer_returning_failure_propagates_error()
    {
        IMultiTableProducer producer = new AlwaysFailingProducer();
        RecipeBuildContext context = MakeContext();

        Result<ImmutableArray<RecipeTable>> result = producer.Produce(context);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
        Assert.Contains("forced failure", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void IMultiTableProducer_can_return_empty_array_of_tables()
    {
        IMultiTableProducer producer = new EmptyMultiProducer();
        RecipeBuildContext context = MakeContext();

        Result<ImmutableArray<RecipeTable>> result = producer.Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // ── MsiRecipeBuilder integration ──────────────────────────────────────────

    [Fact]
    public void MsiRecipeBuilder_with_multi_producer_appends_tables_to_recipe()
    {
        // A fake multi-producer that emits two custom-schema tables. The builder
        // should append them after the built-in single-table producers.
        IMultiTableProducer[] multiProducers = [new FakeDoubleTableProducer()];
        ResolvedPackage resolved = MakeResolvedPackage();

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            multiProducers);

        Assert.True(result.IsSuccess);
        // Built-in count (36) + 2 from FakeDoubleTableProducer = 38.
        Assert.Equal(38, result.Value.Tables.Length);
        Assert.Contains(result.Value.Tables, t => t.Name.Value == "FakeAlpha");
        Assert.Contains(result.Value.Tables, t => t.Name.Value == "FakeBeta");
    }

    [Fact]
    public void MsiRecipeBuilder_with_no_multi_producers_behaves_identically_to_original()
    {
        ResolvedPackage resolved = MakeResolvedPackage();

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            []);

        Assert.True(result.IsSuccess);
        Assert.Equal(36, result.Value.Tables.Length);
    }

    [Fact]
    public void MsiRecipeBuilder_multi_producer_failure_propagates_as_build_failure()
    {
        IMultiTableProducer[] multiProducers = [new AlwaysFailingProducer()];
        ResolvedPackage resolved = MakeResolvedPackage();

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            multiProducers);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
    }

    [Fact]
    public void MsiRecipeBuilder_multi_producer_streams_flow_into_recipe_streams()
    {
        // A fake multi-producer that emits a table with a stream-backed binary cell.
        IMultiTableProducer[] multiProducers = [new StreamEmittingMultiProducer()];
        ResolvedPackage resolved = MakeResolvedPackage();

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            multiProducers);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Streams.ContainsKey("FakeStream"));
    }

    [Fact]
    public void MsiRecipeBuilder_backwards_compatible_three_param_overload_still_works()
    {
        // The original three-argument Build overload must continue to work so
        // existing call sites are not broken by the Phase A change.
        ResolvedPackage resolved = MakeResolvedPackage();

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal(36, result.Value.Tables.Length);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RecipeBuildContext MakeContext()
        => new(
            MakeResolvedPackage(),
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());

    private static ResolvedPackage MakeResolvedPackage()
        => new()
        {
            Package = new PackageModel
            {
                Name = "Test",
                Manufacturer = "M",
                Version = new System.Version(1, 0, 0),
            },
            Components = [],
            Files = [],
        };

    // ── Fakes ─────────────────────────────────────────────────────────────────

    /// <summary>Returns two tables with minimal valid schemas and no rows.</summary>
    private sealed class FakeDoubleTableProducer : IMultiTableProducer
    {
        public Result<ImmutableArray<RecipeTable>> Produce(RecipeBuildContext context)
        {
            RecipeTable alpha = MakeEmptyTable("FakeAlpha");
            RecipeTable beta = MakeEmptyTable("FakeBeta");
            return Result<ImmutableArray<RecipeTable>>.Success(
                ImmutableArray.Create(alpha, beta));
        }

        // Builds a minimal one-column table with no rows.
        internal static RecipeTable MakeEmptyTable(string name)
        {
            TableId tableId = TableId.Create(name).Value;
            ImmutableArray<RecipeColumn> cols =
            [
                new RecipeColumn
                {
                    Name = "Id",
                    Type = ColumnType.String,
                    Width = 72,
                    Nullable = false,
                    LocalizableKey = false,
                },
            ];
            return new RecipeTable
            {
                Name = tableId,
                Columns = cols,
                Rows = ImmutableArray<RecipeRow>.Empty,
                PrimaryKey = [new ColumnIndex(0)],
                CreateTableSql = $"CREATE TABLE `{name}` (`Id` CHAR(72) NOT NULL) PRIMARY KEY `Id`",
                InsertViewSql = $"SELECT `Id` FROM `{name}`",
            };
        }
    }

    /// <summary>Returns failure immediately.</summary>
    private sealed class AlwaysFailingProducer : IMultiTableProducer
    {
        public Result<ImmutableArray<RecipeTable>> Produce(RecipeBuildContext context)
            => Result<ImmutableArray<RecipeTable>>.Failure(
                ErrorKind.CompilationError,
                "IMultiTableProducer forced failure");
    }

    /// <summary>Returns an empty table array — valid degenerate case.</summary>
    private sealed class EmptyMultiProducer : IMultiTableProducer
    {
        public Result<ImmutableArray<RecipeTable>> Produce(RecipeBuildContext context)
            => Result<ImmutableArray<RecipeTable>>.Success(ImmutableArray<RecipeTable>.Empty);
    }

    /// <summary>Emits one table and registers a stream in the shared registry.</summary>
    private sealed class StreamEmittingMultiProducer : IMultiTableProducer
    {
        public Result<ImmutableArray<RecipeTable>> Produce(RecipeBuildContext context)
        {
            byte[] bytes = [0xDE, 0xAD, 0xBE, 0xEF];
            byte[] sha256 = System.Security.Cryptography.SHA256.HashData(bytes);
            StreamSource source = new StreamSource.InMemory(bytes, sha256);
            context.Streams.Register("FakeStream", source);

            // Build a single-column binary table with one row pointing to the stream.
            TableId tableId = TableId.Create("FakeStreamTable").Value;
            ImmutableArray<RecipeColumn> cols =
            [
                new RecipeColumn
                {
                    Name = "Data",
                    Type = ColumnType.Binary,
                    Width = 0,
                    Nullable = false,
                    LocalizableKey = false,
                },
            ];
            ImmutableArray<RecipeRow> rows =
            [
                new RecipeRow { Cells = [new CellValue.StreamRef("FakeStream")] },
            ];
            RecipeTable table = new()
            {
                Name = tableId,
                Columns = cols,
                Rows = rows,
                PrimaryKey = [new ColumnIndex(0)],
                CreateTableSql = "CREATE TABLE `FakeStreamTable` (`Data` OBJECT NOT NULL) PRIMARY KEY `Data`",
                InsertViewSql = "SELECT `Data` FROM `FakeStreamTable`",
            };
            return Result<ImmutableArray<RecipeTable>>.Success([table]);
        }
    }

}
