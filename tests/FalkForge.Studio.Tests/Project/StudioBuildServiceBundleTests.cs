using System.IO;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Project;

public class StudioBuildServiceBundleTests
{
    [Fact]
    public void CompileBundle_NullBundleSettings_ReturnsValidationError()
    {
        var project = StudioProjectLoader.NewProject();
        project.ProjectType = "bundle";
        project.BundleSettings = null;

        var result = StudioBuildService.Compile(project, ".");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("Bundle settings", result.Error.Message);
    }

    [Fact]
    public void CompileBundle_MissingBundleName_ReturnsValidationError()
    {
        var project = StudioProjectLoader.NewProject();
        project.ProjectType = "bundle";
        project.BundleSettings = new BundleSettingsSection
        {
            Name = "",
            Manufacturer = "Corp",
            Version = "1.0.0"
        };

        var result = StudioBuildService.Compile(project, ".");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("name", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompileBundle_ValidSettings_RoutesToBundleCompiler()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk_bundle_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var project = StudioProjectLoader.NewProject();
            project.ProjectType = "bundle";
            project.BundleSettings = new BundleSettingsSection
            {
                Name = "TestBundle",
                Manufacturer = "TestCorp",
                Version = "1.0.0"
            };

            // No packages → BundleCompiler will produce a valid (empty) bundle
            var result = StudioBuildService.Compile(project, tempDir);

            // The compiler should attempt compilation (may succeed or fail at payload stage,
            // but should NOT return NotSupported)
            Assert.NotEqual(ErrorKind.NotSupported, result.IsFailure ? result.Error.Kind : default);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
