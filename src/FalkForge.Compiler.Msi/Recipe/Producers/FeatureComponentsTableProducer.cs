using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>FeatureComponents</c> junction table. Emits one
/// row per (feature, component) pairing, mirroring
/// <see cref="TableEmitter"/>'s <c>EmitFeatureComponents</c> helper. When a
/// resolved component does not declare an explicit <see cref="ResolvedComponent.FeatureRef"/>
/// the producer falls back to the first declared feature id, or <c>"Complete"</c>
/// if the package has no features — matching the legacy default.
/// </summary>
internal sealed class FeatureComponentsTableProducer : ITableProducer
{
    private const string FallbackFeatureId = "Complete";

    /// <summary>Static schema describing the <c>FeatureComponents</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId featureTable = TableId.Create("Feature").Value;
        TableId componentTable = TableId.Create("Component").Value;

        ResolvedPackage resolved = context.Resolved;
        string defaultFeatureId =
            resolved.Package.Features.Count > 0
                ? resolved.Package.Features[0].Id
                : FallbackFeatureId;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (ResolvedComponent component in resolved.Components)
        {
            string featureId = component.FeatureRef ?? defaultFeatureId;

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.ForeignKey(featureTable, featureId),
                new CellValue.ForeignKey(componentTable, component.Id));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId featureTable = TableId.Create("Feature").Value;
        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Feature_",
                Type = ColumnType.String,
                Width = 38,
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
            });

        return new TableSchema
        {
            Name = TableId.Create("FeatureComponents").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(0),
                    TargetTable = featureTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = componentTable,
                }),
        };
    }
}
