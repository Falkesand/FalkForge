using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>ProgId</c> table — the ProgId-table slice of the
/// legacy <see cref="Tables.TableEmitter"/>'s <c>EmitFileAssociations</c>
/// partition. Unlike the MIME and Verb branches, ProgId fires for every
/// <see cref="FileAssociationModel"/> regardless of <c>ContentType</c> or
/// <see cref="FileAssociationModel.Verbs"/>; cells project as
/// (<c>ProgId</c>, <c>ProgId_Parent</c>=null, <c>Class_</c>=null,
/// <c>Description</c>, <c>Icon_</c>=null, <c>IconIndex</c>) to match the
/// legacy <see cref="MsiRecord"/> writes which pin three columns to the
/// literal null because <see cref="FileAssociationModel"/> has no field
/// for them.
///
/// Note: dedicated producers for <c>Extension</c>, <c>MIME</c>, and
/// <c>Verb</c> handle the other three tables in the
/// <c>EmitFileAssociations</c> partition and are intentionally out of
/// scope here so the producer set partitions the input list cleanly.
/// </summary>
internal sealed class ProgIdTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>ProgId</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<FileAssociationModel> associations =
            context.Resolved.Package.FileAssociations;

        if (associations.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(associations.Count);
        foreach (FileAssociationModel assoc in associations)
        {
            CellValue descriptionCell = assoc.Description is null
                ? new CellValue.Null()
                : new CellValue.StringValue(assoc.Description);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(assoc.ProgId),
                new CellValue.Null(),
                new CellValue.Null(),
                descriptionCell,
                new CellValue.Null(),
                new CellValue.IntValue(assoc.IconIndex));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "ProgId",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "ProgId_Parent",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Class_",
                Type = ColumnType.String,
                Width = 38,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Description",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Icon_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "IconIndex",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("ProgId").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
