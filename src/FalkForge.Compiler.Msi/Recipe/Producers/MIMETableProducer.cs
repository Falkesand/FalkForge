using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>MIME</c> table — one of four tables the legacy
/// <see cref="Tables.TableEmitter"/>'s <c>EmitFileAssociations</c> writes
/// per <see cref="FileAssociationModel"/>. This producer covers only the
/// MIME emission: an entry contributes a row when
/// <see cref="FileAssociationModel.ContentType"/> is non-empty. Cells
/// project to (<c>ContentType</c>, <c>Extension_</c> with the leading
/// dot stripped to match the Extension table's bare-suffix primary key,
/// <c>CLSID</c>=null since FileAssociationModel has no CLSID field).
///
/// Note: dedicated producers for <c>ProgId</c>, <c>Extension</c>, and
/// <c>Verb</c> handle the other three tables in the
/// EmitFileAssociations partition and are intentionally out of scope
/// here so the producer set partitions the input list cleanly.
/// </summary>
internal sealed class MIMETableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>MIME</c> table layout.</summary>
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

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (FileAssociationModel assoc in associations)
        {
            if (string.IsNullOrEmpty(assoc.ContentType))
            {
                continue;
            }

            string ext = assoc.Extension.TrimStart('.');

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(assoc.ContentType),
                new CellValue.StringValue(ext),
                new CellValue.Null());
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("ContentType", 64),
            RecipeColumn.String("Extension_", 255),
            RecipeColumn.String("CLSID", 38, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.MIME,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
