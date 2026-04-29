using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Media</c> table. Phase-4 batch-2 scope is the
/// simple single-cabinet case: one row with <c>DiskId = 1</c>,
/// <c>LastSequence</c> equal to the file count (clamped to 1 for empty
/// packages), and the embedded default cabinet name <c>#Data.cab</c>. The
/// MediaTemplate-driven multi-cabinet plan handled by
/// <c>EmitMediaFromTemplate</c> in the legacy emitter is deliberately
/// deferred — cabinet planning lives in a later phase that owns
/// <see cref="MsiRecipeBuildOptions"/>.
/// </summary>
internal sealed class MediaTableProducer : ITableProducer
{
    // Match CabinetBuilder.DefaultCabinetFileName ("cab1.cab") inline to keep the
    // recipe pipeline cross-platform — the cabinet builder itself is Windows-only,
    // but the Media row is just a string the producer emits.
    private const string DefaultEmbeddedCabinetName = "#cab1.cab";

    /// <summary>Static schema describing the <c>Media</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int lastSequence = context.Resolved.Files.Count;
        if (lastSequence == 0)
        {
            lastSequence = 1;
        }

        ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
            new CellValue.IntValue(1),
            new CellValue.IntValue(lastSequence),
            new CellValue.StringValue(string.Empty),
            new CellValue.StringValue(DefaultEmbeddedCabinetName),
            new CellValue.StringValue(string.Empty),
            new CellValue.StringValue(string.Empty));

        ImmutableArray<RecipeRow> rows = ImmutableArray.Create(
            new RecipeRow { Cells = cells });

        return Result<ImmutableArray<RecipeRow>>.Success(rows);
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "DiskId",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "LastSequence",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DiskPrompt",
                Type = ColumnType.Localized,
                Width = 64,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Cabinet",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "VolumeLabel",
                Type = ColumnType.String,
                Width = 32,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Source",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Media").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
