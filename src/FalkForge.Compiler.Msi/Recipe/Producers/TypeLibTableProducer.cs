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
        TableId componentTable = TableId.Create("Component").Value;
        TableId featureTable = TableId.Create("Feature").Value;

        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "LibID",
                Type = ColumnType.String,
                Width = 38,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Language",
                Type = ColumnType.Integer,
                Width = 2,
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
            },
            new RecipeColumn
            {
                // LONG in MSI DDL; nullable per DDL but producer always sets it
                Name = "Version",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Description",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Directory_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
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
                // LONG in MSI DDL; nullable per DDL; legacy emitter always writes 0
                Name = "Cost",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("TypeLib").Value,
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
