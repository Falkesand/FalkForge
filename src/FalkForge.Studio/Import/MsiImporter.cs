using System.Runtime.Versioning;
using FalkForge.Decompiler;
using FalkForge.Models;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Import;

/// <summary>
/// Imports an MSI file into a <see cref="StudioProject"/> by decompiling it
/// via <see cref="MsiDecompiler"/> and mapping the resulting <see cref="PackageModel"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiImporter
{
    /// <summary>
    /// Imports an MSI file and converts it to a <see cref="StudioProject"/>.
    /// </summary>
    public static Result<StudioProject> Import(string msiPath)
    {
        var decompiler = new MsiDecompiler();
        var result = decompiler.Decompile(msiPath);

        if (result.IsFailure)
            return Result<StudioProject>.Failure(result.Error);

        return FromPackageModel(result.Value);
    }

    /// <summary>
    /// Converts a <see cref="PackageModel"/> to a <see cref="StudioProject"/>.
    /// Exposed as internal for unit testing without requiring an actual MSI file.
    /// </summary>
    internal static Result<StudioProject> FromPackageModel(PackageModel model)
    {
        var project = new StudioProject
        {
            ProjectType = "msi",
            Product = MapProduct(model),
            InstallDirectory = model.DefaultInstallDirectory?.ToString(),
            Features = MapFeatures(model),
            Registry = MapRegistry(model.RegistryEntries),
            Services = MapServices(model.Services),
            Shortcuts = MapShortcuts(model.Shortcuts),
            Environment = MapEnvironment(model.EnvironmentVariables),
            CustomActions = MapCustomActions(model.CustomActions)
        };

        return project;
    }

    private static ProductSection MapProduct(PackageModel model)
    {
        return new ProductSection
        {
            Name = model.Name,
            Manufacturer = model.Manufacturer,
            Version = model.Version.ToString(),
            UpgradeCode = model.UpgradeCode != Guid.Empty ? model.UpgradeCode.ToString() : null,
            Architecture = model.Architecture.ToString().ToLowerInvariant(),
            Scope = model.Scope == InstallScope.PerUser ? "perUser" : "perMachine",
            Description = model.Description,
            Comments = model.Comments,
            HelpUrl = model.HelpUrl,
            AboutUrl = model.AboutUrl,
            UpdateUrl = model.UpdateUrl,
            LicenseFile = model.LicenseFile
        };
    }

    private static List<FeatureSection> MapFeatures(PackageModel model)
    {
        // Build a lookup from ComponentId to FileEntryModel for assigning files to features
        var componentFiles = new Dictionary<string, List<FileEntryModel>>(StringComparer.Ordinal);
        foreach (var file in model.Files)
        {
            if (file.ComponentId is not null)
            {
                if (!componentFiles.TryGetValue(file.ComponentId, out var list))
                {
                    list = [];
                    componentFiles[file.ComponentId] = list;
                }

                list.Add(file);
            }
        }

        return model.Features.Select(f => MapFeature(f, componentFiles)).ToList();
    }

    private static FeatureSection MapFeature(
        FeatureModel feature,
        Dictionary<string, List<FileEntryModel>> componentFiles)
    {
        var files = new List<FileEntry>();
        foreach (var componentRef in feature.ComponentRefs)
        {
            if (componentFiles.TryGetValue(componentRef, out var componentFileList))
            {
                files.AddRange(componentFileList.Select(f => new FileEntry
                {
                    Source = f.SourcePath,
                    TargetDirectory = f.TargetDirectory.ToString(),
                    Vital = f.Vital
                }));
            }
        }

        return new FeatureSection
        {
            Id = feature.Id,
            Title = feature.Title,
            Description = feature.Description,
            IsDefault = feature.IsDefault,
            IsRequired = feature.IsRequired,
            InstallLevel = feature.DisplayLevel,
            Files = files,
            Features = feature.Children.Count > 0
                ? feature.Children.Select(c => MapFeature(c, componentFiles)).ToList()
                : null
        };
    }

    private static List<RegistryEntrySection> MapRegistry(IReadOnlyList<RegistryEntryModel> entries)
    {
        return entries.Select(r => new RegistryEntrySection
        {
            Root = r.Root.ToString(),
            Key = r.Key,
            ValueName = r.ValueName ?? "",
            ValueType = r.ValueType.ToString(),
            Value = r.Value?.ToString() ?? ""
        }).ToList();
    }

    private static List<ServiceSection> MapServices(IReadOnlyList<ServiceModel> services)
    {
        return services.Select(s => new ServiceSection
        {
            Name = s.Name,
            DisplayName = s.DisplayName,
            Executable = s.Executable,
            Description = s.Description,
            StartMode = s.StartMode.ToString(),
            Account = s.Account.ToString()
        }).ToList();
    }

    private static List<ShortcutSection> MapShortcuts(IReadOnlyList<ShortcutModel> shortcuts)
    {
        return shortcuts.Select(s => new ShortcutSection
        {
            Name = s.Name,
            TargetFile = s.TargetFile,
            Desktop = s.Locations.Contains(ShortcutLocation.Desktop),
            StartMenu = s.Locations.Contains(ShortcutLocation.StartMenu),
            Startup = s.Locations.Contains(ShortcutLocation.Startup),
            Arguments = s.Arguments,
            Description = s.Description,
            IconFile = s.IconFile,
            WorkingDirectory = s.WorkingDirectory,
            StartMenuSubfolder = s.StartMenuSubfolder
        }).ToList();
    }

    private static List<EnvironmentVariableSection> MapEnvironment(
        IReadOnlyList<EnvironmentVariableModel> variables)
    {
        return variables.Select(v => new EnvironmentVariableSection
        {
            Name = v.Name,
            Value = v.Value,
            Action = v.Action.ToString(),
            IsSystem = v.IsSystem
        }).ToList();
    }

    private static List<CustomActionSection> MapCustomActions(IReadOnlyList<CustomActionModel> actions)
    {
        return actions.Select(a => new CustomActionSection
        {
            Id = a.Id,
            Source = a.SourceRef,
            Target = a.Target,
            Condition = a.Condition,
            Sequence = a.Sequence,
            After = a.After,
            Before = a.Before
        }).ToList();
    }
}
