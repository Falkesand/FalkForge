using System.IO;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using EnvironmentVariableAction = FalkForge.Models.EnvironmentVariableAction;

namespace FalkForge.Studio.Project;

public static class StudioBuildService
{
    public static Result<PackageModel> BuildModel(StudioProject project, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(project.Product.Name))
            return Result<PackageModel>.Failure(ErrorKind.Validation, "Product name is required.");

        if (string.IsNullOrWhiteSpace(project.Product.Manufacturer))
            return Result<PackageModel>.Failure(ErrorKind.Validation, "Product manufacturer is required.");

        if (!Version.TryParse(project.Product.Version, out var version))
            return Result<PackageModel>.Failure(ErrorKind.Validation,
                $"Invalid version format: '{project.Product.Version}'.");

        Guid? upgradeCode = null;
        if (!string.IsNullOrWhiteSpace(project.Product.UpgradeCode))
        {
            if (!Guid.TryParse(project.Product.UpgradeCode, out var parsed))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    $"Invalid upgrade code: '{project.Product.UpgradeCode}'.");
            upgradeCode = parsed;
        }

        if (!TryParseArchitecture(project.Product.Architecture, out var architecture))
            return Result<PackageModel>.Failure(ErrorKind.Validation,
                $"Invalid architecture: '{project.Product.Architecture}'.");

        if (!TryParseScope(project.Product.Scope, out var scope))
            return Result<PackageModel>.Failure(ErrorKind.Validation,
                $"Invalid scope: '{project.Product.Scope}'.");

        if (!TryParseDialogSet(project.Ui.DialogSet, out var dialogSet))
            return Result<PackageModel>.Failure(ErrorKind.Validation,
                $"Invalid dialog set: '{project.Ui.DialogSet}'.");

        if (!TryParseCompression(project.Build.Compression, out var compression))
            return Result<PackageModel>.Failure(ErrorKind.Validation,
                $"Invalid compression level: '{project.Build.Compression}'.");

        foreach (var feature in project.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Id))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    "Feature id is required. Each feature must have a non-empty id.");
        }

        var builder = new PackageBuilder
        {
            Name = project.Product.Name,
            Manufacturer = project.Product.Manufacturer,
            Version = version,
            UpgradeCode = upgradeCode,
            Scope = scope,
            Architecture = architecture,
            Compression = compression,
            Description = project.Product.Description
        };

        if (!string.IsNullOrWhiteSpace(project.InstallDirectory))
            builder.DefaultInstallDirectory = KnownFolder.ProgramFiles / project.InstallDirectory;

        if (!string.IsNullOrWhiteSpace(project.Ui.LicenseFile))
            builder.LicenseFile = Path.IsPathRooted(project.Ui.LicenseFile)
                ? project.Ui.LicenseFile
                : Path.Combine(baseDirectory, project.Ui.LicenseFile);

        builder.UseDialogSet(dialogSet);

        foreach (var feature in project.Features)
        {
            builder.Feature(feature.Id, fb =>
            {
                fb.Title = feature.Title;
                fb.Description = feature.Description;
                fb.IsDefault = feature.IsDefault;
                fb.IsRequired = feature.IsRequired;

                if (feature.Files.Count > 0)
                {
                    fb.Files(fs =>
                    {
                        var installDir = builder.DefaultInstallDirectory
                                         ?? KnownFolder.ProgramFiles / project.Product.Manufacturer / project.Product.Name;

                        foreach (var file in feature.Files)
                        {
                            var sourcePath = Path.IsPathRooted(file.Source)
                                ? file.Source
                                : Path.Combine(baseDirectory, file.Source);

                            fs.Add(sourcePath);
                        }

                        fs.To(installDir);
                    });
                }

                ConfigureSubFeatures(fb, feature.Features, builder, project, baseDirectory);
            });
        }

        foreach (var entry in project.Registry)
        {
            if (!Enum.TryParse<RegistryRoot>(entry.Root, ignoreCase: true, out var root))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    $"Invalid registry root: '{entry.Root}'.");

            if (!Enum.TryParse<RegistryValueType>(entry.ValueType, ignoreCase: true, out var valueType))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    $"Invalid registry value type: '{entry.ValueType}'.");

            builder.Registry(r => r.Key(root, entry.Key, k => k.Value(entry.ValueName, entry.Value, valueType)));
        }

        foreach (var svc in project.Services)
        {
            if (string.IsNullOrWhiteSpace(svc.Name))
                return Result<PackageModel>.Failure(ErrorKind.Validation, "Service name is required.");

            if (!Enum.TryParse<ServiceStartMode>(svc.StartMode, ignoreCase: true, out var startMode))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    $"Invalid service start mode: '{svc.StartMode}'.");

            if (!Enum.TryParse<ServiceAccount>(svc.Account, ignoreCase: true, out var account))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    $"Invalid service account: '{svc.Account}'.");

            builder.Service(svc.Name, s =>
            {
                s.DisplayName = svc.DisplayName;
                s.Executable = svc.Executable;
                s.Description = svc.Description;
                s.StartMode = startMode;
                s.Account = account;
            });

            builder.ServiceControl(sc =>
            {
                sc.ServiceName(svc.Name);
                if (svc.StartOnInstall) sc.StartOnInstall();
                if (svc.StopOnUninstall) sc.StopOnUninstall();
            });
        }

        foreach (var shortcut in project.Shortcuts)
        {
            if (string.IsNullOrWhiteSpace(shortcut.Name))
                return Result<PackageModel>.Failure(ErrorKind.Validation, "Shortcut name is required.");

            if (string.IsNullOrWhiteSpace(shortcut.TargetFile))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    $"Shortcut '{shortcut.Name}' requires a target file.");

            var sb = builder.Shortcut(shortcut.Name, shortcut.TargetFile);
            if (shortcut.Desktop) sb.OnDesktop();
            if (shortcut.StartMenu) sb.OnStartMenu(shortcut.StartMenuSubfolder);
            if (shortcut.Startup) sb.OnStartup();
            if (!string.IsNullOrWhiteSpace(shortcut.Arguments)) sb.WithArguments(shortcut.Arguments);
            if (!string.IsNullOrWhiteSpace(shortcut.Description)) sb.WithDescription(shortcut.Description);
            if (!string.IsNullOrWhiteSpace(shortcut.IconFile)) sb.WithIcon(shortcut.IconFile, 0);
            if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)) sb.WithWorkingDirectory(shortcut.WorkingDirectory);
        }

        foreach (var env in project.Environment)
        {
            if (string.IsNullOrWhiteSpace(env.Name))
                return Result<PackageModel>.Failure(ErrorKind.Validation, "Environment variable name is required.");

            if (!Enum.TryParse<EnvironmentVariableAction>(env.Action, ignoreCase: true, out var action))
                return Result<PackageModel>.Failure(ErrorKind.Validation,
                    $"Invalid environment variable action: '{env.Action}'.");

            builder.EnvironmentVariable(env.Name, env.Value, e =>
            {
                e.IsSystem = env.IsSystem;
                e.Action = action;
            });
        }

        foreach (var ca in project.CustomActions)
        {
            if (string.IsNullOrWhiteSpace(ca.Id))
                return Result<PackageModel>.Failure(ErrorKind.Validation, "Custom action id is required.");

            builder.CustomAction(ca.Id, cab =>
            {
                switch (ca.Type)
                {
                    case "DllFromBinary":
                        cab.DllFromBinary(ca.Source, ca.Target ?? "");
                        break;
                    case "ExeFromBinary":
                        cab.ExeFromBinary(ca.Source);
                        break;
                    case "SetProperty":
                        cab.SetProperty(ca.Source, ca.Target ?? "");
                        break;
                }

                if (ca.Deferred) cab.Deferred();
                if (ca.Rollback) cab.Rollback();
                if (ca.Commit) cab.Commit();
                if (ca.NoImpersonate) cab.NoImpersonate();
                if (ca.ContinueOnError) cab.ContinueOnError();

                cab.Condition = ca.Condition;
                cab.Sequence = ca.Sequence;
                cab.After = ca.After;
                cab.Before = ca.Before;
            });
        }

        var model = builder.Build();
        return Result<PackageModel>.Success(model);
    }

    public static Result<string> Compile(StudioProject project, string baseDirectory)
    {
        var modelResult = BuildModel(project, baseDirectory);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        var outputPath = Path.IsPathRooted(project.Build.OutputPath)
            ? project.Build.OutputPath
            : Path.Combine(baseDirectory, project.Build.OutputPath);

        var compiler = new MsiCompiler();
        return compiler.Compile(modelResult.Value, outputPath);
    }

    public static Result<BundleModel> BuildBundleModel(StudioProject project, string baseDirectory)
    {
        if (project.BundleSettings is null)
            return Result<BundleModel>.Failure(ErrorKind.Validation, "Bundle settings are required.");

        var settings = project.BundleSettings;

        if (string.IsNullOrWhiteSpace(settings.Name))
            return Result<BundleModel>.Failure(ErrorKind.Validation, "Bundle name is required.");

        if (string.IsNullOrWhiteSpace(settings.Manufacturer))
            return Result<BundleModel>.Failure(ErrorKind.Validation, "Bundle manufacturer is required.");

        if (!Version.TryParse(settings.Version, out _))
            return Result<BundleModel>.Failure(ErrorKind.Validation,
                $"Invalid version format: '{settings.Version}'.");

        if (!TryParseBundleScope(settings.Scope, out var scope))
            return Result<BundleModel>.Failure(ErrorKind.Validation,
                $"Invalid scope: '{settings.Scope}'.");

        if (!TryParseBundleUiType(settings.UiType, out var uiType))
            return Result<BundleModel>.Failure(ErrorKind.Validation,
                $"Invalid UI type: '{settings.UiType}'.");

        var builder = new BundleBuilder()
            .Name(settings.Name)
            .Manufacturer(settings.Manufacturer)
            .Version(settings.Version)
            .Scope(scope)
            .DownloadThrottle(settings.DownloadThrottle);

        if (!string.IsNullOrWhiteSpace(settings.UpgradeCode))
        {
            if (!Guid.TryParse(settings.UpgradeCode, out var upgradeCode))
                return Result<BundleModel>.Failure(ErrorKind.Validation,
                    $"Invalid upgrade code: '{settings.UpgradeCode}'.");
            builder.UpgradeCode(upgradeCode);
        }

        switch (uiType)
        {
            case BundleUiType.BuiltIn:
                var licenseFile = !string.IsNullOrWhiteSpace(settings.LicenseFile)
                    ? Path.IsPathRooted(settings.LicenseFile)
                        ? settings.LicenseFile
                        : Path.Combine(baseDirectory, settings.LicenseFile)
                    : null;
                builder.UseBuiltInUI(licenseFile);
                break;
            case BundleUiType.Silent:
                builder.UseSilentUI();
                break;
        }

        foreach (var pkg in project.BundlePackages)
        {
            if (string.IsNullOrWhiteSpace(pkg.Id))
                return Result<BundleModel>.Failure(ErrorKind.Validation,
                    "Bundle package id is required.");

            if (!Enum.TryParse<BundlePackageType>(pkg.Type, ignoreCase: true, out var pkgType))
                return Result<BundleModel>.Failure(ErrorKind.Validation,
                    $"Invalid bundle package type: '{pkg.Type}'.");

            if (!Enum.TryParse<DetectionMode>(pkg.DetectionMode, ignoreCase: true, out var detectionMode))
                return Result<BundleModel>.Failure(ErrorKind.Validation,
                    $"Invalid detection mode: '{pkg.DetectionMode}'.");

            var sourcePath = Path.IsPathRooted(pkg.SourcePath)
                ? pkg.SourcePath
                : Path.Combine(baseDirectory, pkg.SourcePath);

            var capturedPkg = pkg;
            builder.Chain(chain => AddPackageToChain(chain, pkgType, sourcePath, capturedPkg, detectionMode));
        }

        var model = builder.Build();
        return Result<BundleModel>.Success(model);
    }

    private static void ConfigureSubFeatures(
        FeatureBuilder parentBuilder,
        List<FeatureSection>? subFeatures,
        PackageBuilder packageBuilder,
        StudioProject project,
        string baseDirectory)
    {
        if (subFeatures is null) return;

        foreach (var sub in subFeatures)
        {
            parentBuilder.Feature(sub.Id, fb =>
            {
                fb.Title = sub.Title;
                fb.Description = sub.Description;
                fb.IsDefault = sub.IsDefault;
                fb.IsRequired = sub.IsRequired;

                if (sub.Files.Count > 0)
                {
                    fb.Files(fs =>
                    {
                        var installDir = packageBuilder.DefaultInstallDirectory
                                         ?? KnownFolder.ProgramFiles / project.Product.Manufacturer / project.Product.Name;

                        foreach (var file in sub.Files)
                        {
                            var sourcePath = Path.IsPathRooted(file.Source)
                                ? file.Source
                                : Path.Combine(baseDirectory, file.Source);

                            fs.Add(sourcePath);
                        }

                        fs.To(installDir);
                    });
                }

                ConfigureSubFeatures(fb, sub.Features, packageBuilder, project, baseDirectory);
            });
        }
    }

    private static bool TryParseArchitecture(string value, out ProcessorArchitecture result)
    {
        return Enum.TryParse(value, ignoreCase: true, out result);
    }

    private static bool TryParseScope(string value, out InstallScope result)
    {
        return Enum.TryParse(value, ignoreCase: true, out result);
    }

    private static bool TryParseDialogSet(string value, out MsiDialogSet result)
    {
        return Enum.TryParse(value, ignoreCase: true, out result);
    }

    private static bool TryParseCompression(string value, out CompressionLevel result)
    {
        return Enum.TryParse(value, ignoreCase: true, out result);
    }

    private static void AddPackageToChain(
        ChainBuilder chain,
        BundlePackageType pkgType,
        string sourcePath,
        BundlePackageSection pkg,
        DetectionMode detectionMode)
    {
        Action<BundlePackageBuilder> configure = p =>
        {
            p.Id(pkg.Id);
            p.DisplayName(pkg.DisplayName);
            p.Vital(pkg.Vital);
            p.DetectionMode(detectionMode);
            p.Prerequisite(pkg.IsPrerequisite);
            if (!string.IsNullOrWhiteSpace(pkg.InstallCondition))
                p.InstallCondition(pkg.InstallCondition);
            if (!string.IsNullOrWhiteSpace(pkg.AuthenticodeThumbprint))
                p.AuthenticodeThumbprint(pkg.AuthenticodeThumbprint);
        };

        switch (pkgType)
        {
            case BundlePackageType.MsiPackage:
                chain.MsiPackage(sourcePath, configure);
                break;
            case BundlePackageType.ExePackage:
                chain.ExePackage(sourcePath, configure);
                break;
            case BundlePackageType.NetRuntime:
                chain.NetRuntime(sourcePath, configure);
                break;
            default:
                chain.MsiPackage(sourcePath, configure);
                break;
        }
    }

    private static bool TryParseBundleScope(string value, out InstallScope result)
    {
        return Enum.TryParse(value, ignoreCase: true, out result);
    }

    private static bool TryParseBundleUiType(string value, out BundleUiType result)
    {
        return Enum.TryParse(value, ignoreCase: true, out result);
    }
}
