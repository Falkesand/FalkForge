using System.IO;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Project;

public class StudioProjectLoaderTests
{
    [Fact]
    public void NewProject_HasDefaults()
    {
        var project = StudioProjectLoader.NewProject();
        Assert.Equal("msi", project.ProjectType);
        Assert.Equal("My Application", project.Product.Name);
        Assert.NotNull(project.Product.UpgradeCode);
        Assert.Single(project.Features);
        Assert.Equal("Main", project.Features[0].Id);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var project = new StudioProject
        {
            ProjectType = "msi",
            Product = new ProductSection
            {
                Name = "TestApp",
                Manufacturer = "TestCorp",
                Version = "2.0.0",
                UpgradeCode = "12345678-1234-1234-1234-123456789012",
                Architecture = "x86",
                Scope = "perUser",
                Description = "A test application"
            },
            InstallDirectory = "TestCorp/TestApp",
            Features =
            [
                new FeatureSection
                {
                    Id = "Core",
                    Title = "Core Files",
                    IsDefault = true,
                    IsRequired = true,
                    Files = [new FileEntry { Source = "bin/*.dll", TargetDirectory = "bin" }]
                }
            ],
            Ui = new UiSection { DialogSet = "FeatureTree", LicenseFile = "license.rtf" },
            Build = new BuildSection { OutputPath = "dist/", Compression = "Medium" }
        };

        var json = StudioProjectLoader.Serialize(project);
        var loaded = StudioProjectLoader.Deserialize(json);

        Assert.Equal("TestApp", loaded.Product.Name);
        Assert.Equal("TestCorp", loaded.Product.Manufacturer);
        Assert.Equal("2.0.0", loaded.Product.Version);
        Assert.Equal("x86", loaded.Product.Architecture);
        Assert.Equal("perUser", loaded.Product.Scope);
        Assert.Equal("A test application", loaded.Product.Description);
        Assert.Equal("TestCorp/TestApp", loaded.InstallDirectory);
        Assert.Single(loaded.Features);
        Assert.Equal("Core", loaded.Features[0].Id);
        Assert.Single(loaded.Features[0].Files);
        Assert.Equal("bin/*.dll", loaded.Features[0].Files[0].Source);
        Assert.Equal("FeatureTree", loaded.Ui.DialogSet);
        Assert.Equal("license.rtf", loaded.Ui.LicenseFile);
        Assert.Equal("dist/", loaded.Build.OutputPath);
        Assert.Equal("Medium", loaded.Build.Compression);
    }

    [Fact]
    public void RoundTrip_File_PreservesContent()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "FileTest";

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.ffstudio");
        try
        {
            StudioProjectLoader.SaveToFile(project, tempFile);
            var loaded = StudioProjectLoader.LoadFromFile(tempFile);
            Assert.Equal("FileTest", loaded.Product.Name);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void NestedFeatures_RoundTrip()
    {
        var project = StudioProjectLoader.NewProject();
        project.Features =
        [
            new FeatureSection
            {
                Id = "Root", Title = "Root",
                Features =
                [
                    new FeatureSection { Id = "Child", Title = "Child Feature" }
                ]
            }
        ];

        var json = StudioProjectLoader.Serialize(project);
        var loaded = StudioProjectLoader.Deserialize(json);
        Assert.NotNull(loaded.Features[0].Features);
        Assert.Single(loaded.Features[0].Features!);
        Assert.Equal("Child", loaded.Features[0].Features![0].Id);
    }
}
