using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Environment</c> table. Walks
/// <see cref="PackageModel.EnvironmentVariables"/> and emits one row per
/// variable, mirroring the legacy <c>TableEmitter</c> (deleted in Phase 9) <c>EmitEnvironment</c>.
/// Variable name and value strings are pre-encoded via
/// <see cref="EnvironmentEncoding"/> so the legacy install-time encoding
/// (system/user prefix, action sigils, separator handling) is preserved.
/// Synthesises sequential <c>ENV_NNNN</c> identifiers. An ungated entry falls
/// back to the first resolved component (or <c>"MainComponent"</c>); a
/// feature-gated entry (declared via <c>FeatureBuilder.EnvironmentVariable(...)</c>)
/// instead binds to the dedicated component ComponentResolver synthesized for
/// it — see <see cref="ResolveComponentId"/>.
/// </summary>
internal sealed class EnvironmentTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>Static schema describing the <c>Environment</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = WellKnownTableIds.Component;
        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<EnvironmentVariableModel> envVars = resolved.Package.EnvironmentVariables;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        if (envVars.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        for (int index = 0; index < envVars.Count; index++)
        {
            EnvironmentVariableModel envVar = envVars[index];
            string envId = string.Create(CultureInfo.InvariantCulture, $"ENV_{index:D4}");
            string encodedName = EnvironmentEncoding.EncodeName(envVar.Name, envVar.Action, envVar.IsSystem);
            string encodedValue = EnvironmentEncoding.EncodeValue(envVar.Value, envVar.Action, envVar.Separator);
            string componentId = ResolveComponentId(envVar, index, resolved, defaultComponentId);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(envId),
                new CellValue.StringValue(encodedName),
                new CellValue.StringValue(encodedValue),
                new CellValue.ForeignKey(componentTable, componentId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    /// <summary>
    /// Resolves the Component_ FK for an environment-variable row. An entry with a FeatureRef
    /// (declared via FeatureBuilder.EnvironmentVariable(...)) attaches to the dedicated component
    /// ComponentResolver synthesized for it — that component carries the FeatureRef, which is
    /// what places it under the correct feature in FeatureComponents. Without one, the entry
    /// falls back to the first resolved component (or "MainComponent"), matching the legacy
    /// default.
    /// </summary>
    private static string ResolveComponentId(
        EnvironmentVariableModel envVar, int index, ResolvedPackage resolved, string defaultComponentId)
    {
        if (envVar.FeatureRef is not null &&
            resolved.EnvironmentFeatureComponents.TryGetValue(index, out string? featureComponentId))
        {
            return featureComponentId;
        }

        return defaultComponentId;
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Environment", 72),
            RecipeColumn.Localized("Name", 255),
            RecipeColumn.Localized("Value", 0, nullable: true),
            RecipeColumn.String("Component_", 72));

        return new TableSchema
        {
            Name = WellKnownTableIds.Environment,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(3),
                TargetTable = componentTable,
            }),
        };
    }
}
