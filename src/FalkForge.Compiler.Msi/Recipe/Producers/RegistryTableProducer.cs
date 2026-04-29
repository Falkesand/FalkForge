using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Registry</c> table. Walks
/// <see cref="PackageModel.RegistryEntries"/> and projects each entry onto
/// the column shape used by <see cref="TableEmitter"/>'s
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

        TableId componentTable = TableId.Create("Component").Value;
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
            index++;

            string componentId = entry.ComponentId ?? defaultComponentId;
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
        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Registry",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Root",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Key",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Name",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Value",
                Type = ColumnType.Localized,
                Width = 0,
                Nullable = true,
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
            Name = TableId.Create("Registry").Value,
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
