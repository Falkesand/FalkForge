using System.Text.Json;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Cli.Models;
using FalkForge.Models;

namespace FalkForge.Cli;

public static class JsonConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Result<PackageModel> LoadFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return Result<PackageModel>.Failure(new Error(ErrorKind.FileNotFound, $"JSON file not found: {jsonPath}"));

        var json = File.ReadAllText(jsonPath);
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? Environment.CurrentDirectory;
        return LoadFromString(json, baseDirectory);
    }

    public static Result<PackageModel> LoadFromString(string json, string baseDirectory)
    {
        InstallerConfig config;
        try
        {
            config = JsonSerializer.Deserialize<InstallerConfig>(json, JsonOptions)
                ?? new InstallerConfig();
        }
        catch (JsonException ex)
        {
            return Result<PackageModel>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN001: Invalid JSON: {ex.Message}"));
        }

        return BuildPackageModel(config, baseDirectory);
    }

    private static Result<PackageModel> BuildPackageModel(InstallerConfig config, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(config.Product.Name))
            return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "JSN002: Missing required field: product.name"));

        if (string.IsNullOrWhiteSpace(config.Product.Manufacturer))
            return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "JSN003: Missing required field: product.manufacturer"));

        var builder = new PackageBuilder
        {
            Name = config.Product.Name,
            Manufacturer = config.Product.Manufacturer,
        };

        // Version
        if (!string.IsNullOrWhiteSpace(config.Product.Version))
        {
            if (!Version.TryParse(config.Product.Version, out var version))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN004: Invalid version format: {config.Product.Version}"));
            builder.Version = version;
        }

        // UpgradeCode
        if (!string.IsNullOrWhiteSpace(config.Product.UpgradeCode))
        {
            if (!Guid.TryParse(config.Product.UpgradeCode, out var upgradeCode))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN005: Invalid upgrade code GUID: {config.Product.UpgradeCode}"));
            builder.UpgradeCode = upgradeCode;
        }

        // Description
        if (!string.IsNullOrWhiteSpace(config.Product.Description))
            builder.Description = config.Product.Description;

        // Platform
        if (!string.IsNullOrWhiteSpace(config.Product.Platform))
        {
            if (!Enum.TryParse<ProcessorArchitecture>(config.Product.Platform, true, out var arch))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN007: Invalid platform: {config.Product.Platform}. Valid values: X86, X64, Arm64"));
            builder.Architecture = arch;
        }

        // UI Dialog Set
        if (!string.IsNullOrWhiteSpace(config.Ui))
        {
            if (!Enum.TryParse<MsiDialogSet>(config.Ui, true, out var dialogSet))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN006: Invalid UI dialog set: {config.Ui}. Valid values: None, Minimal, InstallDir, FeatureTree, Mondo, Advanced"));
            builder.UseDialogSet(dialogSet);
        }

        // License
        if (!string.IsNullOrWhiteSpace(config.License))
        {
            var licensePath = ResolvePath(config.License, baseDirectory);
            builder.LicenseFile = licensePath;
        }

        // Install Directory
        if (!string.IsNullOrWhiteSpace(config.InstallDirectory))
        {
            builder.DefaultInstallDirectory = KnownFolder.ProgramFiles / config.InstallDirectory;
        }

        // Major Upgrade
        if (config.MajorUpgrade is not null)
        {
            builder.MajorUpgrade(mu =>
            {
                if (!string.IsNullOrWhiteSpace(config.MajorUpgrade.Schedule))
                {
                    if (Enum.TryParse<RemoveExistingProductsSchedule>(config.MajorUpgrade.Schedule, true, out var schedule))
                        mu.Schedule(schedule);
                }
            });
        }

        // Downgrade
        if (config.Downgrade is not null)
        {
            builder.Downgrade(d =>
            {
                if (config.Downgrade.Allow)
                    d.Allow();
                else if (!string.IsNullOrWhiteSpace(config.Downgrade.Message))
                    d.Block(config.Downgrade.Message);
            });
        }

        // Launch Conditions
        if (config.LaunchConditions is not null)
        {
            foreach (var lc in config.LaunchConditions)
            {
                if (!string.IsNullOrWhiteSpace(lc.Condition) && !string.IsNullOrWhiteSpace(lc.Message))
                    builder.Require(lc.Condition, lc.Message);
            }
        }

        // Features
        if (config.Features is not null)
        {
            foreach (var featureConfig in config.Features)
            {
                if (string.IsNullOrWhiteSpace(featureConfig.Id))
                    return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "JSN009: Feature must have an id"));

                var featureResult = ConfigureFeature(builder, featureConfig, baseDirectory);
                if (featureResult.IsFailure)
                    return Result<PackageModel>.Failure(featureResult.Error);
            }
        }

        // Extensions (validate configuration; actual extension compilation is a separate concern)
        if (config.Extensions is not null)
        {
            var extensionResult = ValidateExtensions(config.Extensions);
            if (extensionResult.IsFailure)
                return Result<PackageModel>.Failure(extensionResult.Error);
        }

        try
        {
            var model = builder.Build();
            return Result<PackageModel>.Success(model);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Result<PackageModel>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN010: Configuration error: {ex.Message}"));
        }
    }

    private static Result<Unit> ConfigureFeature(PackageBuilder builder, FeatureConfig featureConfig, string baseDirectory)
    {
        builder.Feature(featureConfig.Id!, fb =>
        {
            if (!string.IsNullOrWhiteSpace(featureConfig.Title))
                fb.Title = featureConfig.Title;

            if (!string.IsNullOrWhiteSpace(featureConfig.Description))
                fb.Description = featureConfig.Description;

            fb.IsDefault = featureConfig.Default;
            fb.IsRequired = featureConfig.Required;

            // Files
            if (featureConfig.Files is not null && featureConfig.Files.Count > 0)
            {
                fb.Files(fs =>
                {
                    fs.To(builder.DefaultInstallDirectory ?? KnownFolder.ProgramFiles / builder.Name);
                    foreach (var file in featureConfig.Files)
                    {
                        if (!string.IsNullOrWhiteSpace(file.Source))
                        {
                            var resolvedPath = ResolvePath(file.Source, baseDirectory);
                            fs.Add(resolvedPath);
                        }
                    }
                });
            }

            // Nested features
            if (featureConfig.Features is not null)
            {
                foreach (var childFeature in featureConfig.Features)
                {
                    if (!string.IsNullOrWhiteSpace(childFeature.Id))
                    {
                        fb.Feature(childFeature.Id, cfb =>
                        {
                            ConfigureNestedFeature(cfb, childFeature, baseDirectory, builder.DefaultInstallDirectory ?? KnownFolder.ProgramFiles / builder.Name);
                        });
                    }
                }
            }
        });

        // Shortcuts (must be configured after feature, at PackageBuilder level)
        if (featureConfig.Files is not null)
        {
            foreach (var file in featureConfig.Files)
            {
                if (file.Shortcut is not null && !string.IsNullOrWhiteSpace(file.Shortcut.Name) && !string.IsNullOrWhiteSpace(file.Source))
                {
                    var shortcutBuilder = builder.Shortcut(file.Shortcut.Name, Path.GetFileName(file.Source));

                    if (!string.IsNullOrWhiteSpace(file.Shortcut.Description))
                        shortcutBuilder.WithDescription(file.Shortcut.Description);

                    if (!string.IsNullOrWhiteSpace(file.Shortcut.Icon))
                        shortcutBuilder.WithIcon(ResolvePath(file.Shortcut.Icon, baseDirectory));

                    var location = file.Shortcut.Location?.ToLowerInvariant() ?? "desktop";
                    switch (location)
                    {
                        case "desktop":
                            shortcutBuilder.OnDesktop();
                            break;
                        case "startmenu":
                            shortcutBuilder.OnStartMenu();
                            break;
                        case "startup":
                            shortcutBuilder.OnStartup();
                            break;
                        default:
                            shortcutBuilder.OnDesktop();
                            break;
                    }
                }
            }
        }

        // Registry
        if (featureConfig.Registry is not null)
        {
            foreach (var reg in featureConfig.Registry)
            {
                if (!string.IsNullOrWhiteSpace(reg.Key) && !string.IsNullOrWhiteSpace(reg.Name))
                {
                    var root = ParseRegistryRoot(reg.Root);
                    builder.Registry(rb => rb.Key(root, reg.Key, kb => kb.Value(reg.Name, reg.Value ?? "")));
                }
            }
        }

        // Services
        if (featureConfig.Services is not null)
        {
            foreach (var svc in featureConfig.Services)
            {
                if (!string.IsNullOrWhiteSpace(svc.Name) && !string.IsNullOrWhiteSpace(svc.Executable))
                {
                    builder.Service(svc.Name, sb =>
                    {
                        sb.Executable = svc.Executable;
                        if (!string.IsNullOrWhiteSpace(svc.DisplayName))
                            sb.DisplayName = svc.DisplayName;
                        if (!string.IsNullOrWhiteSpace(svc.Description))
                            sb.Description = svc.Description;
                        if (!string.IsNullOrWhiteSpace(svc.StartType) &&
                            Enum.TryParse<ServiceStartMode>(svc.StartType, true, out var startMode))
                            sb.StartMode = startMode;
                        if (!string.IsNullOrWhiteSpace(svc.Account) &&
                            Enum.TryParse<ServiceAccount>(svc.Account, true, out var account))
                            sb.Account = account;
                    });
                }
            }
        }

        // Environment Variables
        if (featureConfig.EnvironmentVariables is not null)
        {
            foreach (var env in featureConfig.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(env.Name) && !string.IsNullOrWhiteSpace(env.Value))
                {
                    builder.EnvironmentVariable(env.Name, env.Value, evb =>
                    {
                        evb.IsSystem = env.System;
                        if (!string.IsNullOrWhiteSpace(env.Action) &&
                            Enum.TryParse<EnvironmentVariableAction>(env.Action, true, out var action))
                            evb.Action = action;
                    });
                }
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static void ConfigureNestedFeature(FeatureBuilder fb, FeatureConfig config, string baseDirectory, InstallPath installDirectory)
    {
        if (!string.IsNullOrWhiteSpace(config.Title))
            fb.Title = config.Title;

        if (!string.IsNullOrWhiteSpace(config.Description))
            fb.Description = config.Description;

        fb.IsDefault = config.Default;
        fb.IsRequired = config.Required;

        if (config.Files is not null && config.Files.Count > 0)
        {
            fb.Files(fs =>
            {
                fs.To(installDirectory);
                foreach (var file in config.Files)
                {
                    if (!string.IsNullOrWhiteSpace(file.Source))
                    {
                        var resolvedPath = ResolvePath(file.Source, baseDirectory);
                        fs.Add(resolvedPath);
                    }
                }
            });
        }

        if (config.Features is not null)
        {
            foreach (var childFeature in config.Features)
            {
                if (!string.IsNullOrWhiteSpace(childFeature.Id))
                {
                    fb.Feature(childFeature.Id, cfb =>
                    {
                        ConfigureNestedFeature(cfb, childFeature, baseDirectory, installDirectory);
                    });
                }
            }
        }
    }

    private static RegistryRoot ParseRegistryRoot(string? root)
    {
        return root?.ToUpperInvariant() switch
        {
            "HKLM" or "LOCALMACHINE" => RegistryRoot.LocalMachine,
            "HKCU" or "CURRENTUSER" => RegistryRoot.CurrentUser,
            "HKCR" or "CLASSESROOT" => RegistryRoot.ClassesRoot,
            "HKU" or "USERS" => RegistryRoot.Users,
            _ => RegistryRoot.LocalMachine,
        };
    }

    private static Result<Unit> ValidateExtensions(ExtensionsConfig extensions)
    {
        // Firewall rules
        if (extensions.Firewall is not null)
        {
            for (var i = 0; i < extensions.Firewall.Count; i++)
            {
                var rule = extensions.Firewall[i];

                if (string.IsNullOrWhiteSpace(rule.Id))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN011: Firewall rule at index {i} is missing required field 'id'"));

                if (string.IsNullOrWhiteSpace(rule.Name))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN011: Firewall rule '{rule.Id}' is missing required field 'name'"));

                if (string.IsNullOrWhiteSpace(rule.Port) && string.IsNullOrWhiteSpace(rule.Program))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN011: Firewall rule '{rule.Id}' must specify either 'port' or 'program'"));
            }
        }

        // IIS
        if (extensions.Iis is not null)
        {
            if (extensions.Iis.AppPools is not null)
            {
                for (var i = 0; i < extensions.Iis.AppPools.Count; i++)
                {
                    var pool = extensions.Iis.AppPools[i];

                    if (string.IsNullOrWhiteSpace(pool.Name))
                        return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN012: IIS app pool at index {i} is missing required field 'name'"));
                }
            }

            if (extensions.Iis.WebSites is not null)
            {
                for (var i = 0; i < extensions.Iis.WebSites.Count; i++)
                {
                    var site = extensions.Iis.WebSites[i];

                    if (string.IsNullOrWhiteSpace(site.Description))
                        return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN012: IIS web site at index {i} is missing required field 'description'"));

                    if (site.Bindings is null || site.Bindings.Count == 0)
                        return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN012: IIS web site '{site.Description}' must have at least one binding"));
                }
            }
        }

        // SQL
        if (extensions.Sql is not null)
        {
            for (var i = 0; i < extensions.Sql.Count; i++)
            {
                var sql = extensions.Sql[i];

                if (string.IsNullOrWhiteSpace(sql.Server))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN013: SQL configuration at index {i} is missing required field 'server'"));

                if (string.IsNullOrWhiteSpace(sql.Database))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN013: SQL configuration at index {i} is missing required field 'database'"));
            }
        }

        // .NET detection
        if (extensions.DotNet is not null)
        {
            for (var i = 0; i < extensions.DotNet.Count; i++)
            {
                var dotnet = extensions.DotNet[i];

                if (string.IsNullOrWhiteSpace(dotnet.RuntimeType))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'runtimeType'"));

                if (string.IsNullOrWhiteSpace(dotnet.Platform))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'platform'"));

                if (string.IsNullOrWhiteSpace(dotnet.MinimumVersion))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'minimumVersion'"));

                if (string.IsNullOrWhiteSpace(dotnet.VariableName))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'variableName'"));
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}
