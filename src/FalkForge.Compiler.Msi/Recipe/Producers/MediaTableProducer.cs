using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Cabinets;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Media</c> table. Uses <see cref="CabinetPlanner"/>
/// as the single source of truth for cabinet splitting so the Media rows and
/// the cabinet streams embedded by <see cref="MsiAuthoring"/> are always
/// consistent. When no <c>MediaTemplate</c> is set, <c>CabinetPlanner</c>
/// produces a single <c>Data.cab</c> row (embedded). With a template it splits
/// files across one or more cabs according to the template's size limit and
/// naming pattern; each cab produces one Media row. Legacy parity: this
/// mirrors the <c>EmitMediaFromTemplate</c> / <c>EmitMediaDefault</c>
/// logic in the legacy <c>TableEmitter</c>.
/// </summary>
internal sealed class MediaTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Media</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<CabinetPlan> plans = CabinetPlanner.Plan(
            context.Resolved.Files,
            context.Resolved.Package.MediaTemplate);

        // For an empty file list CabinetPlanner returns an empty plan list.
        // The Media table must still contain one row so the MSI is valid.
        if (plans.Count == 0)
        {
            // No files: emit a single placeholder row matching legacy behaviour
            // (DiskId=1, LastSequence=1, Cabinet=embedded default).
            ImmutableArray<CellValue> placeholderCells = ImmutableArray.Create<CellValue>(
                new CellValue.IntValue(1),
                new CellValue.IntValue(1),
                new CellValue.StringValue(string.Empty),
                new CellValue.StringValue("#" + CabinetPlanner.DefaultCabinetFileName),
                new CellValue.StringValue(string.Empty),
                new CellValue.StringValue(string.Empty));

            return Result<ImmutableArray<RecipeRow>>.Success(
                ImmutableArray.Create(new RecipeRow { Cells = placeholderCells }));
        }

        ImmutableArray<RecipeRow>.Builder rowBuilder =
            ImmutableArray.CreateBuilder<RecipeRow>(plans.Count);

        foreach (CabinetPlan plan in plans)
        {
            // The Cabinet column value carries a '#' prefix when the cabinet is
            // embedded as a _Streams entry; external cabs use the plain file name.
            string cabinetColumnValue = plan.Embedded
                ? "#" + plan.CabinetFileName
                : plan.CabinetFileName;

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.IntValue(plan.DiskId),
                new CellValue.IntValue(plan.LastSequence),
                new CellValue.StringValue(string.Empty),
                new CellValue.StringValue(cabinetColumnValue),
                new CellValue.StringValue(string.Empty),
                new CellValue.StringValue(string.Empty));

            rowBuilder.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rowBuilder.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.Integer("DiskId", 2),
            RecipeColumn.Integer("LastSequence", 2),
            RecipeColumn.Localized("DiskPrompt", 64, nullable: true),
            RecipeColumn.String("Cabinet", 255, nullable: true),
            RecipeColumn.String("VolumeLabel", 32, nullable: true),
            RecipeColumn.String("Source", 72, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.Media,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
