using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>MsiAssemblyName</c> table — mirrors the legacy
/// <see cref="Tables.TableEmitter"/>'s <c>EmitAssemblies</c> MsiAssemblyName rows.
/// For each <see cref="AssemblyModel"/> one row is emitted per non-empty attribute
/// field. The attribute-to-name mapping matches the legacy emitter exactly:
/// <list type="bullet">
///   <item><c>name</c> — <see cref="AssemblyModel.AssemblyName"/></item>
///   <item><c>version</c> — <see cref="AssemblyModel.AssemblyVersion"/></item>
///   <item><c>culture</c> — <see cref="AssemblyModel.AssemblyCulture"/></item>
///   <item><c>publicKeyToken</c> — <see cref="AssemblyModel.AssemblyPublicKeyToken"/></item>
///   <item><c>processorArchitecture</c> — <see cref="AssemblyModel.ProcessorArchitecture"/></item>
///   <item><c>type</c> — emitted as <c>"win32"</c> only for
///     <see cref="AssemblyType.Win32Assembly"/>; not present in the legacy emitter
///     but required by the MSI Win32 assembly manifest specification.</item>
/// </list>
/// Component resolution uses the same filename-to-component lookup as
/// <see cref="MsiAssemblyTableProducer"/> (first-match, case-insensitive), falling
/// back to the first resolved component ID or <c>"MainComponent"</c>.
/// </summary>
internal sealed class MsiAssemblyNameTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>Static schema describing the <c>MsiAssemblyName</c> table layout.</summary>
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

        // Filename → component lookup: shared cache on context so both MsiAssembly
        // and MsiAssemblyName producers pay the O(components × files) cost at most once.
        Dictionary<string, ResolvedComponent> fileToComponent =
            context.GetOrBuildFileToComponentMap();

        // Each assembly may produce 0–6 rows (5 attrs + Win32 "type" row); pre-allocate for worst case.
        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(assemblies.Count * 6);

        foreach (AssemblyModel assembly in assemblies)
        {
            fileToComponent.TryGetValue(assembly.FileRef ?? string.Empty, out ResolvedComponent? ownerComp);
            string componentId = ownerComp?.Id ?? defaultComponentId;

            EmitIfSet(rows, componentId, "name",                 assembly.AssemblyName);
            EmitIfSet(rows, componentId, "version",              assembly.AssemblyVersion);
            EmitIfSet(rows, componentId, "culture",              assembly.AssemblyCulture);
            EmitIfSet(rows, componentId, "publicKeyToken",       assembly.AssemblyPublicKeyToken);
            EmitIfSet(rows, componentId, "processorArchitecture", assembly.ProcessorArchitecture);

            // Win32 assemblies require a "type" = "win32" row per the MSI spec.
            if (assembly.Type == AssemblyType.Win32Assembly)
            {
                rows.Add(MakeRow(componentId, "type", "win32"));
            }
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static void EmitIfSet(
        ImmutableArray<RecipeRow>.Builder rows,
        string componentId,
        string name,
        string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            rows.Add(MakeRow(componentId, name, value));
        }
    }

    private static RecipeRow MakeRow(string componentId, string name, string value) =>
        new()
        {
            Cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(componentId),
                new CellValue.StringValue(name),
                new CellValue.StringValue(value)),
        };

    private static TableSchema BuildSchema()
    {
        TableId componentTable = TableId.Create("Component").Value;

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
                Name = "Name",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Value",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("MsiAssemblyName").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0),
                new ColumnIndex(1)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(0),
                    TargetTable = componentTable,
                }),
        };
    }
}
