using System.Collections.Immutable;
using System.IO;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>ServiceInstall</c> table. Walks
/// <see cref="PackageModel.Services"/> and projects each service onto the
/// 13-column row shape used by the legacy <c>TableEmitter</c> (deleted in Phase 9)
/// <c>EmitServices</c>. The component lookup mirrors the legacy emitter:
/// look up the service executable's bare filename in the file→component
/// map and fall back to the first resolved component (or
/// <c>"MainComponent"</c>) when no match is found. Dependency strings are
/// joined with the MSI dependency separator <c>[~]</c>; typed dependencies
/// take precedence over the legacy string list when both are populated.
/// </summary>
internal sealed class ServiceInstallTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private const int ServiceTypeOwnProcess = 16;
    private const int ErrorControlNormal = 1;
    private const int IdentifierMaxLength = 72;
    private const string DependencySeparator = "[~]";

    /// <summary>Static schema describing the <c>ServiceInstall</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = TableId.Create("Component").Value;
        ResolvedPackage resolved = context.Resolved;

        Dictionary<string, string> fileNameToComponent = BuildFileNameLookup(resolved);
        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (ServiceModel service in resolved.Package.Services)
        {
            int startType = MapStartType(service.StartMode);
            string startName = ResolveStartName(service);
            string executableFileName = Path.GetFileName(service.Executable ?? string.Empty);
            string componentId =
                fileNameToComponent.TryGetValue(executableFileName, out string? matched)
                    ? matched
                    : defaultComponentId;

            string svcId = TruncateId($"SVC_{SanitizeId(service.Name)}");
            string? dependencies = BuildDependencyString(service);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(svcId),
                new CellValue.StringValue(service.Name),
                new CellValue.StringValue(service.DisplayName),
                new CellValue.IntValue(ServiceTypeOwnProcess),
                new CellValue.IntValue(startType),
                new CellValue.IntValue(ErrorControlNormal),
                new CellValue.Null(),
                dependencies is null ? new CellValue.Null() : new CellValue.StringValue(dependencies),
                new CellValue.StringValue(startName),
                service.Password is null ? new CellValue.Null() : new CellValue.StringValue(service.Password),
                service.Arguments is null ? new CellValue.Null() : new CellValue.StringValue(service.Arguments),
                new CellValue.ForeignKey(componentTable, componentId),
                service.Description is null ? new CellValue.Null() : new CellValue.StringValue(service.Description));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static Dictionary<string, string> BuildFileNameLookup(ResolvedPackage resolved)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedComponent component in resolved.Components)
        {
            foreach (ResolvedFile file in component.Files)
            {
                map.TryAdd(file.FileName, component.Id);
            }
        }

        return map;
    }

    private static int MapStartType(ServiceStartMode startMode)
    {
        return startMode switch
        {
            ServiceStartMode.Automatic => 2,
            ServiceStartMode.Manual => 3,
            ServiceStartMode.Disabled => 4,
            ServiceStartMode.DelayedAutomatic => 2,
            _ => 2,
        };
    }

    private static string ResolveStartName(ServiceModel service)
    {
        if (service.AccountProperty is not null)
        {
            return service.AccountProperty;
        }

        return service.Account switch
        {
            ServiceAccount.LocalSystem => "LocalSystem",
            ServiceAccount.LocalService => @"NT AUTHORITY\LocalService",
            ServiceAccount.NetworkService => @"NT AUTHORITY\NetworkService",
            ServiceAccount.User => service.UserName ?? string.Empty,
            _ => "LocalSystem",
        };
    }

    private static string? BuildDependencyString(ServiceModel service)
    {
        if (service.TypedDependencies.Count > 0)
        {
            string[] parts = new string[service.TypedDependencies.Count];
            for (int i = 0; i < service.TypedDependencies.Count; i++)
            {
                ServiceDependencyModel dep = service.TypedDependencies[i];
                parts[i] = dep.Group ? "+" + dep.DependsOn : dep.DependsOn;
            }

            return string.Join(DependencySeparator, parts);
        }

        if (service.Dependencies.Count > 0)
        {
            return string.Join(DependencySeparator, service.Dependencies);
        }

        return null;
    }

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

    private static string TruncateId(string id)
    {
        return id.Length > IdentifierMaxLength ? id[..IdentifierMaxLength] : id;
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "ServiceInstall",
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
                Name = "DisplayName",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "ServiceType",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "StartType",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "ErrorControl",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "LoadOrderGroup",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Dependencies",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "StartName",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Password",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
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
                Name = "Component_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Description",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("ServiceInstall").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(11),
                TargetTable = componentTable,
            }),
        };
    }
}
