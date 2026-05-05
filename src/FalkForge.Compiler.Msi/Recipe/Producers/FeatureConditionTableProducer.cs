using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Condition</c> table (feature gating). Walks
/// <see cref="PackageModel.Features"/> recursively (including
/// <see cref="FeatureModel.Children"/>) and emits one row per
/// <see cref="FeatureConditionModel"/>, mirroring the legacy
/// <c>TableEmitter</c> (deleted in Phase 9) <c>EmitFeatureConditions</c>. The composite
/// primary key on <c>(Feature_, Level)</c> matches the legacy schema in
/// <c>MsiTableDefinitions.CreateConditionTable</c>.
/// </summary>
internal sealed class FeatureConditionTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Condition</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId featureTable = TableId.Create("Feature").Value;
        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (FeatureModel feature in context.Resolved.Package.Features)
        {
            EmitFeatureConditions(feature, featureTable, rows);
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static void EmitFeatureConditions(
        FeatureModel feature,
        TableId featureTable,
        ImmutableArray<RecipeRow>.Builder rows)
    {
        foreach (FeatureConditionModel condition in feature.Conditions)
        {
            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.ForeignKey(featureTable, feature.Id),
                new CellValue.IntValue(condition.Level),
                new CellValue.StringValue(condition.Condition));
            rows.Add(new RecipeRow { Cells = cells });
        }

        foreach (FeatureModel child in feature.Children)
        {
            EmitFeatureConditions(child, featureTable, rows);
        }
    }

    private static TableSchema BuildSchema()
    {
        TableId featureTable = TableId.Create("Feature").Value;
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
                Name = "Level",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Condition",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Condition").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(0),
                TargetTable = featureTable,
            }),
        };
    }
}
