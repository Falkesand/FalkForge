using FalkForge;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Project;

public class StudioBuildServiceMsixTests
{
    [Fact]
    public void Compile_MsixProjectType_ReturnsNotSupported()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.ProjectType = "msix";

        var result = StudioBuildService.Compile(project, ".");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotSupported, result.Error.Kind);
        Assert.Contains("not yet supported", result.Error.Message);
    }

    [Fact]
    public void Compile_MsiProjectType_DoesNotReturnNotSupported()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.ProjectType = "msi";

        var result = StudioBuildService.Compile(project, ".");

        // MSI compilation will fail without actual MSI infrastructure,
        // but it must not fail with NotSupported — it should route to MSI path.
        if (result.IsFailure)
            Assert.NotEqual(ErrorKind.NotSupported, result.Error.Kind);
    }

    [Fact]
    public void Compile_NullProjectType_RoutesToMsi()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.ProjectType = null!;

        var result = StudioBuildService.Compile(project, ".");

        // Null project type should default to MSI path, not NotSupported.
        if (result.IsFailure)
            Assert.NotEqual(ErrorKind.NotSupported, result.Error.Kind);
    }

    [Fact]
    public void Compile_BundleProjectType_ReturnsNotSupported()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.ProjectType = "bundle";

        var result = StudioBuildService.Compile(project, ".");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.NotSupported, result.Error.Kind);
        Assert.Contains("not yet supported", result.Error.Message);
    }
}
