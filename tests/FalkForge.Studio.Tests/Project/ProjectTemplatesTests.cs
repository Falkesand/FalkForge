using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Project;

public class ProjectTemplatesTests
{
    [Fact]
    public void ProjectTemplates_All_ContainsFiveTemplates()
    {
        Assert.Equal(5, ProjectTemplates.All.Count);
    }

    [Fact]
    public void ProjectTemplates_EmptyProject_HasOneFeature()
    {
        var template = ProjectTemplates.All.First(t => t.Name == "Empty Project");
        var project = template.Create();

        Assert.Single(project.Features);
        Assert.Equal("Main", project.Features[0].Id);
        Assert.Equal("msi", project.ProjectType);
    }

    [Fact]
    public void ProjectTemplates_DesktopApp_HasShortcut()
    {
        var template = ProjectTemplates.All.First(t => t.Name == "Desktop Application");
        var project = template.Create();

        Assert.Single(project.Shortcuts);
        Assert.Equal("My Desktop App", project.Shortcuts[0].Name);
        Assert.True(project.Shortcuts[0].StartMenu);
    }

    [Fact]
    public void ProjectTemplates_WindowsService_HasService()
    {
        var template = ProjectTemplates.All.First(t => t.Name == "Windows Service");
        var project = template.Create();

        Assert.Single(project.Services);
        Assert.Equal("MyService", project.Services[0].Name);
        Assert.Equal("My Service", project.Services[0].DisplayName);
        Assert.True(project.Services[0].StartOnInstall);
        Assert.True(project.Services[0].StopOnUninstall);
    }

    [Fact]
    public void ProjectTemplates_Bundle_SetsProjectType()
    {
        var template = ProjectTemplates.All.First(t => t.Name == "EXE Bundle");
        var project = template.Create();

        Assert.Equal("bundle", project.ProjectType);
        Assert.NotNull(project.BundleSettings);
        Assert.Equal("My Product Suite", project.BundleSettings!.Name);
    }
}
