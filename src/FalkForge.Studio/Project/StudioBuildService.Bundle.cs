using System.IO;
using FalkForge;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;

namespace FalkForge.Studio.Project;

public static partial class StudioBuildService
{
    /// <summary>
    /// Builds the <see cref="BundleModel"/> from the project's bundle settings.
    /// </summary>
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

    private static Result<string> CompileBundle(StudioProject project, string baseDirectory, string outputPath)
    {
        var modelResult = BuildBundleModel(project, baseDirectory);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        var compiler = new BundleCompiler();
        return compiler.Compile(modelResult.Value, outputPath);
    }
}
