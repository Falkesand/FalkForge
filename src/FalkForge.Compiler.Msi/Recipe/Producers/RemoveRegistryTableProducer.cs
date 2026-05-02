using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>RemoveRegistry</c> table. Walks
/// <see cref="PackageModel.RemoveRegistryEntries"/> and emits one row per
/// entry, mirroring <see cref="Tables.TableEmitter"/>'s
/// <c>EmitRemoveRegistry</c>: the <c>Root</c> enum is mapped to the MSI
/// integer, the <c>Name</c> column is forced to null when the action is
/// <see cref="RemoveRegistryAction.RemoveKey"/> (so MSI removes the entire
/// key rather than a single value), and the component foreign key falls back
/// to the first resolved component (or <c>"MainComponent"</c>) when the model
/// does not pin one explicitly.
/// </summary>
internal sealed class RemoveRegistryTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>Static schema describing the <c>RemoveRegistry</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = TableId.Create("Component").Value;
        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<RemoveRegistryModel> entries = resolved.Package.RemoveRegistryEntries;

        if (entries.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>(entries.Count);
        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        foreach (RemoveRegistryModel entry in entries)
        {
            int root = MapRoot(entry.Root);
            string componentId = entry.ComponentRef ?? defaultComponentId;
            string? effectiveName =
                entry.Action == RemoveRegistryAction.RemoveKey ? null : entry.Name;

            CellValue nameCell = effectiveName is null
                ? new CellValue.Null()
                : new CellValue.StringValue(effectiveName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(entry.Id),
                new CellValue.IntValue(root),
                new CellValue.StringValue(entry.Key),
                nameCell,
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
                Name = "RemoveRegistry",
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
                Name = "Component_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("RemoveRegistry").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(4),
                TargetTable = componentTable,
            }),
        };
    }
}
