using System.IO;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Integration;

public class BuildIntegrationTests
{
    [Fact]
    public void FullPipeline_CreatesModel_WithCorrectProperties()
    {
        var project = new StudioProject
        {
            Product = new ProductSection
            {
                Name = "IntegrationTest",
                Manufacturer = "TestCorp",
                Version = "1.2.3",
                UpgradeCode = Guid.NewGuid().ToString(),
                Architecture = "x64",
                Scope = "perMachine",
                Description = "Integration test product"
            },
            InstallDirectory = "TestCorp/IntegrationTest",
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main Application",
                    IsDefault = true,
                    IsRequired = true
                }
            ],
            Ui = new UiSection { DialogSet = "Minimal" },
            Build = new BuildSection { OutputPath = "out/", Compression = "High" }
        };

        var result = StudioBuildService.BuildModel(project, Path.GetTempPath());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("IntegrationTest", result.Value.Name);
        Assert.Equal("TestCorp", result.Value.Manufacturer);
        Assert.Equal(new Version("1.2.3"), result.Value.Version);
        Assert.Equal(FalkForge.ProcessorArchitecture.X64, result.Value.Architecture);
        Assert.Equal(FalkForge.InstallScope.PerMachine, result.Value.Scope);
        Assert.Equal(FalkForge.Models.MsiDialogSet.Minimal, result.Value.DialogSet);
        Assert.Equal(FalkForge.CompressionLevel.High, result.Value.Compression);
    }

    [Fact]
    public void ProjectRoundTrip_ThenBuild_Succeeds()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "RoundTripApp";
        project.Product.Manufacturer = "TestCorp";

        var json = StudioProjectLoader.Serialize(project);
        var loaded = StudioProjectLoader.Deserialize(json);

        var result = StudioBuildService.BuildModel(loaded, ".");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("RoundTripApp", result.Value.Name);
    }
}
