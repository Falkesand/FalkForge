using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>File</c> table. Walks
/// <see cref="ResolvedPackage.Files"/> in order and emits one row per file.
/// Sequence numbers are assigned by walking the flat file list starting at
/// 1 — matches the convention in <see cref="TableEmitter.EmitFiles"/>.
/// Phase-4 scope: real cabinet-driven sequencing strategies arrive in later
/// phases via <see cref="MsiRecipeBuildOptions.Sequencing"/>; for now the
/// producer uses ordinal index. Version/Language are emitted as null since
/// <see cref="ResolvedFile"/> exposes neither today; Attributes is set to
/// 512 (msidbFileAttributesVital) to match the legacy emitter.
/// </summary>
internal sealed class FileTableProducer : ITableProducer
{
    private const int FileAttributesVital = 512;

    /// <summary>Static schema describing the <c>File</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        int sequence = 1;
        foreach (ResolvedFile file in context.Resolved.Files)
        {
            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(file.FileId),
                new CellValue.ForeignKey(componentTable, file.ComponentId),
                new CellValue.StringValue(file.FileName),
                new CellValue.IntValue(checked((int)file.FileSize)),
                new CellValue.Null(),
                new CellValue.Null(),
                new CellValue.IntValue(FileAttributesVital),
                new CellValue.IntValue(sequence));
            rows.Add(new RecipeRow { Cells = cells });
            sequence++;
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "File",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Component_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "FileName",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "FileSize",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Version",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Language",
                Type = ColumnType.String,
                Width = 20,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Attributes",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Sequence",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("File").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(1),
                TargetTable = componentTable,
            }),
        };
    }
}
