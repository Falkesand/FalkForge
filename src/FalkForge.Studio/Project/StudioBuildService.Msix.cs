using System.IO;
using FalkForge;
using FalkForge.Compiler.Msix;
using FalkForge.Compiler.Msix.Builders;
using FalkForge.Models;

namespace FalkForge.Studio.Project;

public static partial class StudioBuildService
{
    /// <summary>
    /// Builds the <see cref="MsixModel"/> from the project settings.
    /// MSIX flattens the feature tree — all feature files are collected into a single file set.
    /// </summary>
    internal static Result<MsixModel> BuildMsixModel(StudioProject project, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(project.Product.Name))
            return Result<MsixModel>.Failure(ErrorKind.Validation, "Product name is required.");

        if (string.IsNullOrWhiteSpace(project.Product.Manufacturer))
            return Result<MsixModel>.Failure(ErrorKind.Validation, "Product manufacturer is required.");

        if (!Version.TryParse(project.Product.Version, out var version))
            return Result<MsixModel>.Failure(ErrorKind.Validation,
                $"Invalid version format: '{project.Product.Version}'.");

        // MSIX requires a 4-part version number.
        if (version.Build < 0)
            version = new Version(version.Major, version.Minor, 0, 0);
        else if (version.Revision < 0)
            version = new Version(version.Major, version.Minor, version.Build, 0);

        var publisher = project.Product.Manufacturer;
        if (!publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            publisher = $"CN={publisher}";

        var displayName = publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)
            ? publisher.Substring(3)
            : publisher;

        if (!TryParseArchitecture(project.Product.Architecture, out var architecture))
            return Result<MsixModel>.Failure(ErrorKind.Validation,
                $"Invalid architecture: '{project.Product.Architecture}'.");

        if (!TryParseScope(project.Product.Scope, out var scope))
            return Result<MsixModel>.Failure(ErrorKind.Validation,
                $"Invalid scope: '{project.Product.Scope}'.");

        var builder = new MsixBuilder()
            .Name(project.Product.Name)
            .Publisher(publisher)
            .Version(version)
            .DisplayName(project.Product.Name)
            .PublisherDisplayName(displayName)
            .Architecture(architecture)
            .Scope(scope);

        if (!string.IsNullOrWhiteSpace(project.Product.Description))
            builder.Description(project.Product.Description);

        // MSIX has no feature tree — flatten all features' files into a single file set.
        var allFiles = new List<FileEntry>();
        CollectAllFiles(project.Features, allFiles);

        if (allFiles.Count > 0)
        {
            builder.Files(fs =>
            {
                var installDir = KnownFolder.ProgramFiles / displayName / project.Product.Name;

                foreach (var file in allFiles)
                {
                    var sourcePath = Path.IsPathRooted(file.Source)
                        ? file.Source
                        : Path.Combine(baseDirectory, file.Source);

                    fs.Add(sourcePath);
                }

                fs.To(installDir);
            });
        }

        var model = builder.Build();
        return Result<MsixModel>.Success(model);
    }

    private static Result<string> CompileMsix(StudioProject project, string baseDirectory, string outputPath)
    {
        if (!OperatingSystem.IsWindows())
            return Result<string>.Failure(ErrorKind.NotSupported, "MSIX compilation requires Windows.");

        var modelResult = BuildMsixModel(project, baseDirectory);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        var compiler = new MsixCompiler();
        return compiler.Compile(modelResult.Value, outputPath);
    }
}
