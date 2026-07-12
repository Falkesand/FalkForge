using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>FeatureComponents</c> junction table. Emits one
/// row per (feature, component) pairing, mirroring the legacy
/// <c>TableEmitter</c> (deleted in Phase 9) <c>EmitFeatureComponents</c> helper. When a
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

        TableId featureTable = WellKnownTableIds.Feature;
        TableId componentTable = WellKnownTableIds.Component;

        ResolvedPackage resolved = context.Resolved;
        string defaultFeatureId =
            resolved.Package.Features.Count > 0
                ? resolved.Package.Features[0].Id
                : FallbackFeatureId;

        // Explicit feature->component wiring authored on FeatureModel.ComponentRefs. The decompiler
        // populates this from an existing MSI's FeatureComponents table; without it a decompile ->
        // recompile round trip would silently drop the mapping (the reconstructed components carry
        // no FeatureRef, so they would all collapse onto the default feature). Walk the whole
        // feature tree so nested child features contribute too.
        var explicitRefs = new List<(string Feature, string Component)>();
        CollectComponentRefs(resolved.Package.Features, explicitRefs);

        // Dangling ComponentRefs must fail the build loudly rather than surfacing later as an
        // opaque MSI foreign-key error (or, worse, silently vanishing).
        if (explicitRefs.Count > 0)
        {
            var componentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ResolvedComponent component in resolved.Components)
            {
                componentIds.Add(component.Id);
            }

            foreach ((string feature, string componentRef) in explicitRefs)
            {
                if (!componentIds.Contains(componentRef))
                {
                    return Result<ImmutableArray<RecipeRow>>.Failure(
                        ErrorKind.Validation,
                        $"Feature '{feature}' references component '{componentRef}' via ComponentRefs, " +
                        "but no component with that id exists in the package.");
                }
            }
        }

        // A component explicitly claimed by a ComponentRefs entry must not also receive the
        // default-feature fallback below — otherwise it would be linked under both the default
        // feature and its declared feature.
        var claimedByRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string _, string componentRef) in explicitRefs)
        {
            claimedByRefs.Add(componentRef);
        }

        // (Feature_, Component_) is the FeatureComponents primary key; dedup so an overlap between
        // the FeatureRef-derived path and an explicit ComponentRefs entry pointing at the same
        // feature yields exactly one row instead of a duplicate-key failure.
        var emitted = new HashSet<(string Feature, string Component)>();
        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();

        void AddRow(string featureId, string componentId)
        {
            if (!emitted.Add((featureId, componentId)))
            {
                return;
            }

            rows.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.ForeignKey(featureTable, featureId),
                    new CellValue.ForeignKey(componentTable, componentId)),
            });
        }

        foreach (ResolvedComponent component in resolved.Components)
        {
            if (component.FeatureRef is not null)
            {
                AddRow(component.FeatureRef, component.Id);
            }
            else if (!claimedByRefs.Contains(component.Id))
            {
                AddRow(defaultFeatureId, component.Id);
            }

            // A FeatureRef-less component claimed by ComponentRefs is emitted below under its
            // declared feature(s) rather than the default.
        }

        foreach ((string feature, string componentRef) in explicitRefs)
        {
            AddRow(feature, componentRef);
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static void CollectComponentRefs(
        IReadOnlyList<FeatureModel> features, List<(string Feature, string Component)> into)
    {
        foreach (FeatureModel feature in features)
        {
            foreach (string componentId in feature.ComponentRefs)
            {
                into.Add((feature.Id, componentId));
            }

            if (feature.Children.Count > 0)
            {
                CollectComponentRefs(feature.Children, into);
            }
        }
    }

    private static TableSchema BuildSchema()
    {
        TableId featureTable = WellKnownTableIds.Feature;
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Feature_", 38),
            RecipeColumn.String("Component_", 72));

        return new TableSchema
        {
            Name = WellKnownTableIds.FeatureComponents,
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
