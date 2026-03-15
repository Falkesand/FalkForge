using FalkForge;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Project;

public class StudioBuildServiceMsixTests
{
    [Fact]
    public void Compile_MsixProjectType_RoutesToMsixCompiler()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.ProjectType = "msix";

        var result = StudioBuildService.Compile(project, ".");

        // MSIX compilation routes to MsixCompiler. Without applications or signing
        // configured, it should fail with Validation (MsixValidator: no applications defined).
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
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
    public void BuildMsixModel_MapsNameAndPublisher()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";

        var result = StudioBuildService.BuildMsixModel(project, ".");

        Assert.True(result.IsSuccess);
        Assert.Equal("TestApp", result.Value.Name);
        Assert.Equal("CN=TestCorp", result.Value.Publisher);
        Assert.Equal("TestApp", result.Value.DisplayName);
        Assert.Equal("TestCorp", result.Value.PublisherDisplayName);
    }

    [Fact]
    public void BuildMsixModel_PreservesExistingCnPrefix()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "CN=TestCorp";

        var result = StudioBuildService.BuildMsixModel(project, ".");

        Assert.True(result.IsSuccess);
        Assert.Equal("CN=TestCorp", result.Value.Publisher);
        Assert.Equal("TestCorp", result.Value.PublisherDisplayName);
    }

    [Fact]
    public void BuildMsixModel_EnsuresFourPartVersion()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.Product.Version = "1.2.3";

        var result = StudioBuildService.BuildMsixModel(project, ".");

        Assert.True(result.IsSuccess);
        Assert.Equal(new Version(1, 2, 3, 0), result.Value.Version);
    }

    [Fact]
    public void BuildMsixModel_MissingName_ReturnsValidationError()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        project.Product.Manufacturer = "TestCorp";

        var result = StudioBuildService.BuildMsixModel(project, ".");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void BuildMsixModel_MissingManufacturer_ReturnsValidationError()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "";

        var result = StudioBuildService.BuildMsixModel(project, ".");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("manufacturer", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMsixModel_IncludesFilesFromAllFeatures()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.Features.Add(new FeatureSection
        {
            Id = "Feature1",
            Files = [new FileEntry { Source = "file1.exe" }]
        });
        project.Features.Add(new FeatureSection
        {
            Id = "Feature2",
            Files = [new FileEntry { Source = "file2.dll" }]
        });

        var result = StudioBuildService.BuildMsixModel(project, ".");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Files.Count);
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
