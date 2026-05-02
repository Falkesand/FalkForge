using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>DuplicateFile</c> table. Walks
/// <see cref="PackageModel.DuplicateFiles"/> and emits one row per entry,
/// mirroring <see cref="Tables.TableEmitter"/>'s <c>EmitDuplicateFiles</c>:
/// each row carries the explicit <see cref="DuplicateFileModel.Id"/> as
/// the <c>FileKey</c> primary key, foreign keys to <c>Component</c> (with
/// the standard <c>ComponentRef</c> ?? first-resolved-component ??
/// <c>"MainComponent"</c> fallback chain) and <c>File</c>, and nullable
/// <c>DestFolder</c> / <c>DestName</c> string cells projecting from the
/// optional <see cref="DuplicateFileModel.DestDirectory"/> and
/// <see cref="DuplicateFileModel.DestFileName"/>.
/// </summary>
internal sealed class DuplicateFileTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private static readonly TableId ComponentTable = TableId.Create("Component").Value;
    private static readonly TableId FileTable = TableId.Create("File").Value;

    /// <summary>Static schema describing the <c>DuplicateFile</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<DuplicateFileModel> duplicateFiles = resolved.Package.DuplicateFiles;

        if (duplicateFiles.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(duplicateFiles.Count);
        foreach (DuplicateFileModel df in duplicateFiles)
        {
            string componentId = df.ComponentRef ?? defaultComponentId;

            CellValue destFolderCell = df.DestDirectory is null
                ? new CellValue.Null()
                : new CellValue.StringValue(df.DestDirectory);
            CellValue destNameCell = df.DestFileName is null
                ? new CellValue.Null()
                : new CellValue.StringValue(df.DestFileName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(df.Id),
                new CellValue.ForeignKey(ComponentTable, componentId),
                new CellValue.ForeignKey(FileTable, df.FileRef),
                destFolderCell,
                destNameCell);
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "FileKey",
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
                Name = "File_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DestFolder",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DestName",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("DuplicateFile").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = ComponentTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(2),
                    TargetTable = FileTable,
                }),
        };
    }
}
