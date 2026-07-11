using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Registry</c> table. Walks
/// <see cref="PackageModel.RegistryEntries"/> and projects each entry onto
/// the column shape used by the legacy <c>TableEmitter</c> (deleted in Phase 9)
/// <c>EmitRegistry</c>. Synthesises sequential <c>Reg_NNNN</c> identifiers
/// matching the legacy emitter and falls back to the first resolved
/// component (or <c>"MainComponent"</c>) when an entry omits an explicit
/// <see cref="RegistryEntryModel.ComponentId"/>.
/// </summary>
internal sealed class RegistryTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>Static schema describing the <c>Registry</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = WellKnownTableIds.Component;
        ResolvedPackage resolved = context.Resolved;
        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        int index = 0;
        foreach (RegistryEntryModel entry in resolved.Package.RegistryEntries)
        {
            int root = MapRoot(entry.Root);
            string regId = string.Create(
                CultureInfo.InvariantCulture,
                $"Reg_{index:D4}");
            string componentId = ResolveComponentId(entry, index, resolved, defaultComponentId);
            index++;

            string valueText = entry.Value?.ToString() ?? string.Empty;

            CellValue nameCell = entry.ValueName is null
                ? new CellValue.Null()
                : new CellValue.StringValue(entry.ValueName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(regId),
                new CellValue.IntValue(root),
                new CellValue.StringValue(entry.Key),
                nameCell,
                new CellValue.StringValue(valueText),
                new CellValue.ForeignKey(componentTable, componentId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    /// <summary>
    /// Resolves the Component_ FK for a registry row. An explicit
    /// <see cref="RegistryEntryModel.ComponentId"/> always wins (strongest, user-authored
    /// override). Otherwise, an entry with a FeatureRef (declared via FeatureBuilder.Registry)
    /// attaches to the dedicated component ComponentResolver synthesized for it — that component
    /// carries the FeatureRef, which is what places it under the correct feature in
    /// FeatureComponents. Without either, the entry falls back to the first resolved component
    /// (or "MainComponent"), matching the legacy TableEmitter default.
    /// </summary>
    private static string ResolveComponentId(
        RegistryEntryModel entry, int index, ResolvedPackage resolved, string defaultComponentId)
    {
        if (entry.ComponentId is not null)
        {
            return entry.ComponentId;
        }

        if (entry.FeatureRef is not null &&
            resolved.RegistryFeatureComponents.TryGetValue(index, out string? featureComponentId))
        {
            return featureComponentId;
        }

        return defaultComponentId;
    }

    private static int MapRoot(RegistryRoot root)
    {
        return root switch
        {
            RegistryRoot.ClassesRoot => 0,
            RegistryRoot.CurrentUser => 1,
            RegistryRoot.LocalMachine => 2,
            RegistryRoot.Users => 3,
            _ => 2,
        };
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Registry", 72),
            RecipeColumn.Integer("Root", 2),
            RecipeColumn.Localized("Key", 255),
            RecipeColumn.Localized("Name", 255, nullable: true),
            RecipeColumn.Localized("Value", 0, nullable: true),
            RecipeColumn.String("Component_", 72));

        return new TableSchema
        {
            Name = WellKnownTableIds.Registry,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(5),
                TargetTable = componentTable,
            }),
        };
    }
}
