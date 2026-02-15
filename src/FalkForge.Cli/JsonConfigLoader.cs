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
                if (config.MajorUpgrade.AllowDowngrades)
                    mu.AllowDowngrades();

                if (!string.IsNullOrWhiteSpace(config.MajorUpgrade.DowngradeMessage))
                    mu.DowngradeErrorMessage(config.MajorUpgrade.DowngradeMessage);

                if (!string.IsNullOrWhiteSpace(config.MajorUpgrade.Schedule))
                {
                    if (Enum.TryParse<RemoveExistingProductsSchedule>(config.MajorUpgrade.Schedule, true, out var schedule))
                        mu.Schedule(schedule);
                }
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

        try
        {
            var model = builder.Build();
            return Result<PackageModel>.Success(model);
        }
        catch (Exception ex)
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
                    fs.To(KnownFolder.ProgramFiles / builder.Name);
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
                            ConfigureNestedFeature(cfb, childFeature, baseDirectory, builder.Name);
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

    private static void ConfigureNestedFeature(FeatureBuilder fb, FeatureConfig config, string baseDirectory, string productName)
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
                fs.To(KnownFolder.ProgramFiles / productName);
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
                        ConfigureNestedFeature(cfb, childFeature, baseDirectory, productName);
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

    private static string ResolvePath(string path, string baseDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}
