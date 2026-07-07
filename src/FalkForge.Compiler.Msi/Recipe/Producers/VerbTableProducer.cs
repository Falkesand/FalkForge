using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Verb</c> table — the Verb-table slice of the
/// legacy <see cref="Tables.TableEmitter"/>'s <c>EmitFileAssociations</c>
/// partition. For each <see cref="FileAssociationModel"/>, one row is
/// emitted per <see cref="VerbModel"/> in
/// <see cref="FileAssociationModel.Verbs"/>. Cells project as
/// (<c>Extension_</c> with the leading dot stripped to match the
/// Extension table's bare-suffix primary key, <c>Verb</c>, <c>Sequence</c>,
/// <c>Command</c>, <c>Argument</c>). Associations with no verbs contribute
/// zero rows, matching the legacy foreach over an empty collection.
///
/// The <c>Extension_</c> FK relationship to the Extension table is
/// conventional (name-based) rather than declared via
/// <see cref="ForeignKeySpec"/>, mirroring the pattern used by all other
/// file-association producers in this namespace.
///
/// Note: dedicated producers for <c>ProgId</c>, <c>Extension</c>, and
/// <c>MIME</c> handle the other three tables in the
/// <c>EmitFileAssociations</c> partition and are intentionally out of
/// scope here so the producer set partitions the input list cleanly.
/// </summary>
internal sealed class VerbTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Verb</c> table layout.</summary>
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
            if (assoc.Verbs.Count == 0)
            {
                continue;
            }

            string ext = assoc.Extension.TrimStart('.');

            foreach (VerbModel verb in assoc.Verbs)
            {
                CellValue commandCell = verb.Command is null
                    ? new CellValue.Null()
                    : new CellValue.StringValue(verb.Command);

                CellValue argumentCell = verb.Argument is null
                    ? new CellValue.Null()
                    : new CellValue.StringValue(verb.Argument);

                ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue(ext),
                    new CellValue.StringValue(verb.Verb),
                    new CellValue.IntValue(verb.Sequence),
                    commandCell,
                    argumentCell);
                rows.Add(new RecipeRow { Cells = cells });
            }
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Extension_", 255),
            RecipeColumn.String("Verb", 32),
            // SHORT maps to ColumnType.Integer with Width=2 — same convention
            // used by ProgIdTableProducer.IconIndex and MediaTableProducer.
            RecipeColumn.Integer("Sequence", 2, nullable: true),
            RecipeColumn.Localized("Command", 255, nullable: true),
            RecipeColumn.Localized("Argument", 255, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.Verb,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
