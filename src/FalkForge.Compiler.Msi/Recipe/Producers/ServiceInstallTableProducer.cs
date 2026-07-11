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

        TableId componentTable = WellKnownTableIds.Component;
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
            string componentId = ResolveComponentId(service, resolved, fileNameToComponent, defaultComponentId);

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

    /// <summary>
    /// Resolves the Component_ FK for a service row. A service with an explicit FeatureRef
    /// (declared via FeatureBuilder.Service) always attaches to the dedicated component
    /// ComponentResolver synthesized for it — that component carries the FeatureRef, which is
    /// what places it under the correct feature in FeatureComponents. Without a FeatureRef the
    /// service falls back to the legacy convention: attach to whichever resolved component owns
    /// a file with the same bare filename as the service executable, or the first resolved
    /// component (or "MainComponent") when no match exists.
    /// </summary>
    private static string ResolveComponentId(
        ServiceModel service,
        ResolvedPackage resolved,
        Dictionary<string, string> fileNameToComponent,
        string defaultComponentId)
    {
        if (service.FeatureRef is not null &&
            resolved.ServiceFeatureComponents.TryGetValue(service.Name, out string? featureComponentId))
        {
            return featureComponentId;
        }

        string executableFileName = Path.GetFileName(service.Executable ?? string.Empty);
        return fileNameToComponent.TryGetValue(executableFileName, out string? matched)
            ? matched
            : defaultComponentId;
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
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("ServiceInstall", 72),
            RecipeColumn.String("Name", 255),
            RecipeColumn.Localized("DisplayName", 255, nullable: true),
            RecipeColumn.Integer("ServiceType", 4),
            RecipeColumn.Integer("StartType", 4),
            RecipeColumn.Integer("ErrorControl", 4),
            RecipeColumn.String("LoadOrderGroup", 255, nullable: true),
            RecipeColumn.String("Dependencies", 255, nullable: true),
            RecipeColumn.String("StartName", 255, nullable: true),
            RecipeColumn.String("Password", 255, nullable: true),
            RecipeColumn.String("Arguments", 255, nullable: true),
            RecipeColumn.String("Component_", 72),
            RecipeColumn.Localized("Description", 255, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.ServiceInstall,
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
