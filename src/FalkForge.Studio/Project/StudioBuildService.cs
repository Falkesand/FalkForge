using System.IO;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using EnvironmentVariableAction = FalkForge.Models.EnvironmentVariableAction;

namespace FalkForge.Studio.Project;

/// <summary>
/// Compiles a <see cref="StudioProject"/> into an MSI, bundle, or MSIX artifact.
/// Split across partial files by output type:
/// <list type="bullet">
///   <item><see cref="StudioBuildService"/> — <see cref="Compile"/> dispatch and shared helpers.</item>
///   <item>StudioBuildService.Msi.cs — <c>BuildModel</c> and <c>CompileMsi</c>.</item>
///   <item>StudioBuildService.Bundle.cs — <c>BuildBundleModel</c> and <c>CompileBundle</c>.</item>
///   <item>StudioBuildService.Msix.cs — <c>BuildMsixModel</c> and <c>CompileMsix</c>.</item>
/// </list>
/// </summary>
public static partial class StudioBuildService
{
    /// <summary>
    /// Compiles the project to its configured output path, dispatching to the
    /// appropriate sub-compiler based on <see cref="StudioProject.ProjectType"/>.
    /// </summary>
    public static Result<string> Compile(StudioProject project, string baseDirectory)
    {
        var outputPath = Path.IsPathRooted(project.Build.OutputPath)
            ? project.Build.OutputPath
            : Path.Combine(baseDirectory, project.Build.OutputPath);

        return project.ProjectType?.ToLowerInvariant() switch
        {
            "msix"   => CompileMsix(project, baseDirectory, outputPath),
            "bundle" => CompileBundle(project, baseDirectory, outputPath),
            _        => CompileMsi(project, baseDirectory, outputPath)
        };
    }

    // ── Shared parse helpers ──────────────────────────────────────────────────
    // Used across MSI, MSIX, and/or Bundle builders. Kept here to avoid
    // duplication across the partial files.

    private static bool TryParseArchitecture(string value, out ProcessorArchitecture result)
        => Enum.TryParse(value, ignoreCase: true, out result);

    private static bool TryParseScope(string value, out InstallScope result)
        => Enum.TryParse(value, ignoreCase: true, out result);

    private static bool TryParseDialogSet(string value, out MsiDialogSet result)
        => Enum.TryParse(value, ignoreCase: true, out result);

    private static bool TryParseCompression(string value, out CompressionLevel result)
        => Enum.TryParse(value, ignoreCase: true, out result);

    private static bool TryParseBundleScope(string value, out InstallScope result)
        => Enum.TryParse(value, ignoreCase: true, out result);

    private static bool TryParseBundleUiType(string value, out BundleUiType result)
        => Enum.TryParse(value, ignoreCase: true, out result);

    // ── Shared feature/file helpers ───────────────────────────────────────────

    /// <summary>
    /// Recursively configures sub-features on a parent <see cref="FeatureBuilder"/>.
    /// </summary>
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
                fb.DisplayLevel = sub.InstallLevel;

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

    /// <summary>
    /// Collects all file entries from a feature tree into <paramref name="allFiles"/>.
    /// Used by MSIX compilation, which has no feature tree concept.
    /// </summary>
    private static void CollectAllFiles(List<FeatureSection> features, List<FileEntry> allFiles)
    {
        foreach (var feature in features)
        {
            allFiles.AddRange(feature.Files);
            if (feature.Features is not null)
                CollectAllFiles(feature.Features, allFiles);
        }
    }

    /// <summary>Adds a bundle package of the specified type to the chain.</summary>
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
}
