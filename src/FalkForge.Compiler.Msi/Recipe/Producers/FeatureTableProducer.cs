using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Feature</c> table. Walks
/// <see cref="PackageModel.Features"/> recursively and emits a row per
/// feature, threading the parent feature ID through the foreign-key column.
/// The Display/Level/Attributes derivation matches the legacy
/// <c>TableEmitter</c> (deleted in Phase 9) <c>EmitFeature</c> helper so the recipe
/// produces identical rows. The Directory_ column is hard-wired to
/// <c>"INSTALLDIR"</c> to mirror the legacy emitter; later phases that
/// surface explicit feature directories will override.
/// </summary>
internal sealed class FeatureTableProducer : ITableProducer
{
    private const int FeatureUiDisallowAbsentAttribute = 16;
    private const int FeatureDisplayDefault = 1;
    private const int FeatureDisplayHidden = 2;
    private const int FeatureLevelInstall = 1;
    private const int FeatureLevelDoNotInstall = 1000;
    private const string DefaultFeatureDirectoryRef = "INSTALLDIR";

    /// <summary>Static schema describing the <c>Feature</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId featureTable = WellKnownTableIds.Feature;
        TableId directoryTable = WellKnownTableIds.Directory;
        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (FeatureModel feature in context.Resolved.Package.Features)
        {
            EmitFeature(feature, parentId: null, featureTable, directoryTable, rows);
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static void EmitFeature(
        FeatureModel feature,
        string? parentId,
        TableId featureTable,
        TableId directoryTable,
        ImmutableArray<RecipeRow>.Builder rows)
    {
        int display = feature.IsDefault ? FeatureDisplayDefault : FeatureDisplayHidden;
        int level = feature.IsRequired
            ? FeatureLevelInstall
            : feature.IsDefault
                ? FeatureLevelInstall
                : FeatureLevelDoNotInstall;
        int attributes = feature.IsRequired ? FeatureUiDisallowAbsentAttribute : 0;

        CellValue parentCell = parentId is null
            ? new CellValue.Null()
            : new CellValue.ForeignKey(featureTable, parentId);
        CellValue titleCell = feature.Title is null
            ? new CellValue.Null()
            : new CellValue.StringValue(feature.Title);
        CellValue descriptionCell = feature.Description is null
            ? new CellValue.Null()
            : new CellValue.StringValue(feature.Description);

        ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
            new CellValue.StringValue(feature.Id),
            parentCell,
            titleCell,
            descriptionCell,
            new CellValue.IntValue(display),
            new CellValue.IntValue(level),
            new CellValue.ForeignKey(directoryTable, DefaultFeatureDirectoryRef),
            new CellValue.IntValue(attributes));
        rows.Add(new RecipeRow { Cells = cells });

        foreach (FeatureModel child in feature.Children)
        {
            EmitFeature(child, feature.Id, featureTable, directoryTable, rows);
        }
    }

    private static TableSchema BuildSchema()
    {
        TableId featureTable = WellKnownTableIds.Feature;
        TableId directoryTable = WellKnownTableIds.Directory;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Feature", 38),
            RecipeColumn.String("Feature_Parent", 38, nullable: true),
            RecipeColumn.Localized("Title", 64, nullable: true),
            RecipeColumn.Localized("Description", 255, nullable: true),
            RecipeColumn.Integer("Display", 2, nullable: true),
            RecipeColumn.Integer("Level", 2),
            RecipeColumn.String("Directory_", 72, nullable: true),
            RecipeColumn.Integer("Attributes", 2));

        return new TableSchema
        {
            Name = featureTable,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = featureTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(6),
                    TargetTable = directoryTable,
                }),
        };
    }
}
