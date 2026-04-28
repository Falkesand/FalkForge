using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Directory</c> table. Walks
/// <see cref="PackageModel.Directories"/> and emits one row per directory.
/// A null <see cref="DirectoryModel.ParentId"/> becomes a null cell;
/// otherwise the parent reference is emitted as a foreign key into the
/// <c>Directory</c> table itself (self-reference). Real directory-tree
/// synthesis (TARGETDIR + INSTALLDIR + per-component synthesis) lives in
/// later phases — this producer is intentionally a thin projection of the
/// already-supplied <see cref="DirectoryModel"/> list.
/// </summary>
internal sealed class DirectoryTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Directory</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (DirectoryModel directory in context.Resolved.Package.Directories)
        {
            CellValue parentCell = directory.ParentId is null
                ? new CellValue.Null()
                : new CellValue.ForeignKey(directoryTable, directory.ParentId);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(directory.Id),
                parentCell,
                new CellValue.StringValue(directory.Name));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Directory",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Directory_Parent",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DefaultDir",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = directoryTable,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(1),
                TargetTable = directoryTable,
            }),
        };
    }
}
