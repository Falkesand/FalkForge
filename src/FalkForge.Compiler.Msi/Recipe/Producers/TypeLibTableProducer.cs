using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>TypeLib</c> table — mirrors the legacy
/// <see cref="Tables.TableEmitter"/>'s <c>EmitTypeLibs</c>. One row is
/// emitted per <see cref="ComTypeLibModel"/> entry. Columns map as:
/// <list type="bullet">
///   <item><c>LibID</c> — <see cref="ComTypeLibModel.TypeLibId"/> formatted
///     with "B" (braces) and uppercased, matching the legacy emitter.</item>
///   <item><c>Language</c> — <see cref="ComTypeLibModel.Language"/> integer.</item>
///   <item><c>Component_</c> — <see cref="ComTypeLibModel.ComponentRef"/> when
///     set; otherwise the first resolved component ID or <c>"MainComponent"</c>
///     fallback, matching the legacy emitter's <c>defaultComponentId</c>.</item>
///   <item><c>Version</c> — <c>(Major &lt;&lt; 8) | Minor</c>, matching the
///     legacy packed-short encoding.</item>
///   <item><c>Description</c> — <see cref="ComTypeLibModel.Description"/>,
///     null when absent.</item>
///   <item><c>Directory_</c> — always null; <see cref="ComTypeLibModel"/> has
///     no directory field and the legacy emitter writes null.</item>
///   <item><c>Feature_</c> — first feature ID or <c>"Complete"</c> fallback,
///     matching the legacy emitter's <c>defaultFeature</c>.</item>
///   <item><c>Cost</c> — always 0, matching the legacy emitter.</item>
/// </list>
/// </summary>
internal sealed class TypeLibTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private const string FallbackFeatureId = "Complete";

    /// <summary>Static schema describing the <c>TypeLib</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<ComTypeLibModel> typeLibs = resolved.Package.TypeLibs;

        if (typeLibs.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        string defaultFeatureId =
            resolved.Package.Features.Count > 0
                ? resolved.Package.Features[0].Id
                : FallbackFeatureId;

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(typeLibs.Count);

        foreach (ComTypeLibModel tl in typeLibs)
        {
            string libId = tl.TypeLibId.ToString("B").ToUpperInvariant();
            string componentId = tl.ComponentRef ?? defaultComponentId;
            int version = (tl.Version.Major << 8) | tl.Version.Minor;

            CellValue descriptionCell = tl.Description is null
                ? new CellValue.Null()
                : new CellValue.StringValue(tl.Description);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(libId),
                new CellValue.IntValue(tl.Language),
                new CellValue.StringValue(componentId),
                new CellValue.IntValue(version),
                descriptionCell,
                new CellValue.Null(),
                new CellValue.StringValue(defaultFeatureId),
                new CellValue.IntValue(0));

            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        TableId featureTable = WellKnownTableIds.Feature;

        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("LibID", 38),
            RecipeColumn.Integer("Language", 2),
            RecipeColumn.String("Component_", 72),
            // LONG in MSI DDL; nullable per DDL but producer always sets it
            RecipeColumn.Integer("Version", 4, nullable: true),
            RecipeColumn.String("Description", 255, nullable: true),
            RecipeColumn.String("Directory_", 72, nullable: true),
            RecipeColumn.String("Feature_", 38),
            // LONG in MSI DDL; nullable per DDL; legacy emitter always writes 0
            RecipeColumn.Integer("Cost", 4, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.TypeLib,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0),
                new ColumnIndex(1),
                new ColumnIndex(2)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(2),
                    TargetTable = componentTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(6),
                    TargetTable = featureTable,
                }),
        };
    }
}
