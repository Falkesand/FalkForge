using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>MsiAssembly</c> table — mirrors the legacy
/// <see cref="Tables.TableEmitter"/>'s <c>EmitAssemblies</c> MsiAssembly rows.
/// One row is emitted per <see cref="AssemblyModel"/> entry. Columns map as:
/// <list type="bullet">
///   <item><c>Component_</c> — component that owns the file referenced by
///     <see cref="AssemblyModel.FileRef"/>, resolved via a file-name-to-component
///     lookup; falls back to the first resolved component ID, or
///     <c>"MainComponent"</c> when no components exist.</item>
///   <item><c>Feature_</c> — the owning component's <c>FeatureRef</c> when set;
///     otherwise the first feature ID, or <c>"Complete"</c> fallback.</item>
///   <item><c>File_Manifest</c> — always null, matching the legacy emitter.</item>
///   <item><c>File_Application</c> — <see cref="AssemblyModel.ApplicationFileRef"/>,
///     null when absent.</item>
///   <item><c>Attributes</c> — <c>(int)assembly.Type</c>: 0 for
///     <see cref="AssemblyType.DotNetAssembly"/>, 1 for
///     <see cref="AssemblyType.Win32Assembly"/>.</item>
/// </list>
/// </summary>
internal sealed class MsiAssemblyTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private const string FallbackFeatureId = "Complete";

    /// <summary>Static schema describing the <c>MsiAssembly</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<AssemblyModel> assemblies = resolved.Package.Assemblies;

        if (assemblies.Count == 0)
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

        // Filename → component lookup: shared cache on context so both MsiAssembly
        // and MsiAssemblyName producers pay the O(components × files) cost at most once.
        Dictionary<string, ResolvedComponent> fileToComponent =
            context.GetOrBuildFileToComponentMap();

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(assemblies.Count);

        foreach (AssemblyModel assembly in assemblies)
        {
            fileToComponent.TryGetValue(assembly.FileRef ?? string.Empty, out ResolvedComponent? ownerComp);
            string componentId = ownerComp?.Id ?? defaultComponentId;
            string featureId = ownerComp?.FeatureRef ?? defaultFeatureId;
            int attributes = (int)assembly.Type;

            CellValue fileApplicationCell = assembly.ApplicationFileRef is null
                ? new CellValue.Null()
                : new CellValue.StringValue(assembly.ApplicationFileRef);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(componentId),
                new CellValue.StringValue(featureId),
                new CellValue.Null(),                // File_Manifest: always null per legacy
                fileApplicationCell,
                new CellValue.IntValue(attributes));

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
                Name = "Component_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
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
                // Nullable per MSI DDL; legacy emitter always writes null
                Name = "File_Manifest",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "File_Application",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // SHORT in MSI DDL; nullable per DDL; producer always sets it
                Name = "Attributes",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("MsiAssembly").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            // File_Manifest (col 2) is always null — FK omitted to avoid misleading
            // validators into expecting a File table row for a column never populated.
            // File_Application (col 3) is optional; no FK declared for nullable refs.
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(0),
                    TargetTable = componentTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = featureTable,
                }),
        };
    }
}
