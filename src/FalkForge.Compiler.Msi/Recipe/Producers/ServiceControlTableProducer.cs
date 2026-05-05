using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>ServiceControl</c> table. Emits two row categories:
/// <list type="bullet">
///   <item>
///     <description>
///       Auto-rows from <see cref="PackageModel.Services"/>: mirrors the behaviour
///       of <see cref="Tables.TableEmitter.EmitServices"/> which inserts a
///       <c>SVC_{name}_Start</c> (Event=1, Wait=1) and <c>SVC_{name}_Stop</c>
///       (Event=2, Wait=1) row into <c>ServiceControl</c> for every
///       <see cref="ServiceModel"/>. The component FK is resolved by matching the
///       service executable basename against resolved file names.
///     </description>
///   </item>
///   <item>
///     <description>
///       Explicit rows from <see cref="PackageModel.ServiceControls"/>: mirrors
///       <c>EmitServiceControls</c>. The <c>Event</c> flags enum is projected to
///       its integer value, the boolean <c>Wait</c> flag becomes 0/1, and the
///       component FK falls back to the first resolved component (or
///       <c>"MainComponent"</c>) when the model does not pin one explicitly.
///     </description>
///   </item>
/// </list>
/// </summary>
internal sealed class ServiceControlTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private const int MaxServiceControlIdLength = 72;

    /// <summary>Static schema describing the <c>ServiceControl</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = TableId.Create("Component").Value;
        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<ServiceModel> services = resolved.Package.Services;
        IReadOnlyList<ServiceControlModel> controls = resolved.Package.ServiceControls;

        if (services.Count == 0 && controls.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        // Build a filename → componentId lookup table for service executable matching,
        // mirroring TableEmitter.EmitServices's executableToComponentId dictionary.
        // Using FrozenDictionary for O(1) reads — this table is built once per Produce call.
        Dictionary<string, string> executableToComponentId =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedComponent component in resolved.Components)
        {
            foreach (ResolvedFile file in component.Files)
            {
                executableToComponentId.TryAdd(file.FileName, component.Id);
            }
        }

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(services.Count * 2 + controls.Count);

        // Auto-rows from Services (mirrors TableEmitter.EmitServices ServiceControl inserts)
        foreach (ServiceModel service in services)
        {
            string executableFileName = Path.GetFileName(service.Executable ?? string.Empty);
            string componentId = executableToComponentId.GetValueOrDefault(executableFileName)
                                 ?? defaultComponentId;

            string svcId = $"SVC_{SanitizeId(service.Name)}";
            if (svcId.Length > MaxServiceControlIdLength)
            {
                svcId = svcId[..MaxServiceControlIdLength];
            }

            rows.Add(MakeRow(componentTable, $"{svcId}_Start", service.Name, 1, null, 1, componentId));
            rows.Add(MakeRow(componentTable, $"{svcId}_Stop", service.Name, 2, null, 1, componentId));
        }

        // Explicit rows from ServiceControls (mirrors TableEmitter.EmitServiceControls)
        foreach (ServiceControlModel control in controls)
        {
            string componentId = control.ComponentRef ?? defaultComponentId;
            rows.Add(MakeRow(
                componentTable,
                control.Id,
                control.ServiceName,
                (int)control.Events,
                control.Arguments,
                control.Wait ? 1 : 0,
                componentId));
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static RecipeRow MakeRow(
        TableId componentTable,
        string id,
        string name,
        int eventValue,
        string? arguments,
        int? wait,
        string componentId)
    {
        ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
            new CellValue.StringValue(id),
            new CellValue.StringValue(name),
            new CellValue.IntValue(eventValue),
            arguments is null ? new CellValue.Null() : new CellValue.StringValue(arguments),
            wait is null ? new CellValue.Null() : new CellValue.IntValue(wait.Value),
            new CellValue.ForeignKey(componentTable, componentId));
        return new RecipeRow { Cells = cells };
    }

    /// <summary>
    /// Mirrors <c>TableEmitter.SanitizeId</c>: replaces any character that is not
    /// a letter, digit, underscore, or dot with an underscore.
    /// </summary>
    private static string SanitizeId(string name)
    {
        char[] sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            sanitized[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
        }

        return new string(sanitized);
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "ServiceControl",
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
                Name = "Event",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Arguments",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Wait",
                Type = ColumnType.Integer,
                Width = 2,
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
            Name = TableId.Create("ServiceControl").Value,
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
