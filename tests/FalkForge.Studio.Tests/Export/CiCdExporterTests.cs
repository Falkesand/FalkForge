using FalkForge.Studio.Export;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Export;

public class CiCdExporterTests
{
    private static StudioProject CreateMsiProject(string name = "TestApp")
    {
        return new StudioProject
        {
            ProjectType = "msi",
            Product = new ProductSection
            {
                Name = name,
                Manufacturer = "TestCorp",
                Version = "1.0.0"
            }
        };
    }

    private static StudioProject CreateBundleProject(string name = "TestBundle")
    {
        return new StudioProject
        {
            ProjectType = "bundle",
            Product = new ProductSection
            {
                Name = name,
                Manufacturer = "TestCorp",
                Version = "1.0.0"
            }
        };
    }

    [Fact]
    public void GitHubActions_ContainsExpectedStructure()
    {
        var project = CreateMsiProject();

        var result = CiCdExporter.Export(project, CiCdPlatform.GitHubActions);

        Assert.True(result.IsSuccess);
        Assert.Contains("name: Build Installer", result.Value);
        Assert.Contains("runs-on: windows-latest", result.Value);
        Assert.Contains("actions/checkout@v4", result.Value);
        Assert.Contains("actions/setup-dotnet@v4", result.Value);
        Assert.Contains("dotnet tool install -g FalkForge.Cli", result.Value);
        Assert.Contains("forge build", result.Value);
        Assert.Contains("actions/upload-artifact@v4", result.Value);
    }

    [Fact]
    public void AzureDevOps_ContainsExpectedStructure()
    {
        var project = CreateMsiProject();

        var result = CiCdExporter.Export(project, CiCdPlatform.AzureDevOps);

        Assert.True(result.IsSuccess);
        Assert.Contains("trigger: [main]", result.Value);
        Assert.Contains("vmImage: 'windows-latest'", result.Value);
        Assert.Contains("UseDotNet@2", result.Value);
        Assert.Contains("dotnet tool install -g FalkForge.Cli", result.Value);
        Assert.Contains("forge build", result.Value);
        Assert.Contains("PublishBuildArtifacts@1", result.Value);
    }

    [Fact]
    public void Jenkins_ContainsExpectedStructure()
    {
        var project = CreateMsiProject();

        var result = CiCdExporter.Export(project, CiCdPlatform.Jenkins);

        Assert.True(result.IsSuccess);
        Assert.Contains("pipeline {", result.Value);
        Assert.Contains("agent { label 'windows' }", result.Value);
        Assert.Contains("stages {", result.Value);
        Assert.Contains("stage('Build')", result.Value);
        Assert.Contains("bat 'forge build", result.Value);
        Assert.Contains("archiveArtifacts", result.Value);
    }

    [Fact]
    public void BundleProject_UsesExeArtifactPattern()
    {
        var project = CreateBundleProject();

        var ghResult = CiCdExporter.Export(project, CiCdPlatform.GitHubActions);
        var azResult = CiCdExporter.Export(project, CiCdPlatform.AzureDevOps);
        var jkResult = CiCdExporter.Export(project, CiCdPlatform.Jenkins);

        Assert.True(ghResult.IsSuccess);
        Assert.Contains("*.exe", ghResult.Value);
        Assert.True(azResult.IsSuccess);
        Assert.Contains("*.exe", azResult.Value);
        Assert.True(jkResult.IsSuccess);
        Assert.Contains("*.exe", jkResult.Value);
    }

    [Fact]
    public void MsiProject_UsesMsiArtifactPattern()
    {
        var project = CreateMsiProject();

        var ghResult = CiCdExporter.Export(project, CiCdPlatform.GitHubActions);
        var azResult = CiCdExporter.Export(project, CiCdPlatform.AzureDevOps);
        var jkResult = CiCdExporter.Export(project, CiCdPlatform.Jenkins);

        Assert.True(ghResult.IsSuccess);
        Assert.Contains("*.msi", ghResult.Value);
        Assert.True(azResult.IsSuccess);
        Assert.Contains("*.msi", azResult.Value);
        Assert.True(jkResult.IsSuccess);
        Assert.Contains("*.msi", jkResult.Value);
    }

    [Fact]
    public void ProjectName_AppearsInOutput()
    {
        var project = CreateMsiProject("My Cool App");

        var result = CiCdExporter.Export(project, CiCdPlatform.GitHubActions);

        Assert.True(result.IsSuccess);
        Assert.Contains("My-Cool-App.ffstudio", result.Value);
    }

    [Fact]
    public void EmptyProductName_ReturnsFailure()
    {
        var project = new StudioProject
        {
            Product = new ProductSection { Name = "", Manufacturer = "Test" }
        };

        var result = CiCdExporter.Export(project, CiCdPlatform.GitHubActions);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }
}
