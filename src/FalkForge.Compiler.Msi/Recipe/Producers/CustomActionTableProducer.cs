using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>CustomAction</c> table. Walks
/// <see cref="PackageModel.CustomActions"/> and emits one row per entry,
/// mirroring the <c>CustomAction</c>-table half of the legacy
/// <see cref="Tables.TableEmitter"/>'s <c>EmitCustomActions</c>:
/// <c>Action</c> from <see cref="CustomActionModel.Id"/>, <c>Type</c> from
/// <see cref="CustomActionModel.Type"/>, <c>Source</c> from
/// <see cref="CustomActionModel.SourceRef"/>, <c>Target</c> from
/// <see cref="CustomActionModel.Target"/> (nullable cell when unset), and
/// <c>ExtendedType</c> hard-coded to <c>0</c> to match the legacy emitter
/// which has no model field for it.
///
/// Note: the legacy <c>EmitCustomActions</c> also writes
/// <c>InstallExecuteSequence</c> rows for actions that pin a sequence,
/// before, or after anchor. That emission is intentionally out of scope
/// for this producer — a dedicated <c>InstallExecuteSequenceTableProducer</c>
/// will project the same source list onto its own table when wired up.
/// </summary>
internal sealed class CustomActionTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>CustomAction</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<CustomActionModel> customActions = context.Resolved.Package.CustomActions;

        if (customActions.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(customActions.Count);
        foreach (CustomActionModel ca in customActions)
        {
            CellValue targetCell = ca.Target is null
                ? new CellValue.Null()
                : new CellValue.StringValue(ca.Target);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(ca.Id),
                new CellValue.IntValue(ca.Type),
                new CellValue.StringValue(ca.SourceRef),
                targetCell,
                new CellValue.IntValue(0));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Action", 72),
            RecipeColumn.Integer("Type", 2),
            RecipeColumn.String("Source", 72, nullable: true),
            RecipeColumn.String("Target", 255, nullable: true),
            RecipeColumn.Integer("ExtendedType", 4, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.CustomAction,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
