using FalkForge.Studio.Diff;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Diff;

public class ProjectDifferTests
{
    [Fact]
    public void IdenticalProjects_ReturnsNoDiffs()
    {
        var left = CreateProject("TestApp", "1.0.0");
        var right = CreateProject("TestApp", "1.0.0");

        var entries = ProjectDiffer.Diff(left, right);

        Assert.Empty(entries);
    }

    [Fact]
    public void ChangedProductName_ReturnsOneModifiedEntry()
    {
        var left = CreateProject("AppA", "1.0.0");
        var right = CreateProject("AppB", "1.0.0");

        var entries = ProjectDiffer.Diff(left, right);

        var nameEntry = Assert.Single(entries, e => e.Path == "product.name");
        Assert.Equal(DiffKind.Modified, nameEntry.Kind);
        Assert.Equal("AppA", nameEntry.LeftValue);
        Assert.Equal("AppB", nameEntry.RightValue);
    }

    [Fact]
    public void AddedFeature_ReturnsAddedEntries()
    {
        var left = new StudioProject
        {
            Features = [new FeatureSection { Id = "Main", Title = "Main" }]
        };
        var right = new StudioProject
        {
            Features =
            [
                new FeatureSection { Id = "Main", Title = "Main" },
                new FeatureSection { Id = "Extra", Title = "Extra Feature" }
            ]
        };

        var entries = ProjectDiffer.Diff(left, right);

        Assert.Contains(entries, e => e.Path.StartsWith("features[1]") && e.Kind == DiffKind.Added);
        Assert.Contains(entries, e => e.Path == "features[1].id" && e.RightValue == "Extra");
    }

    [Fact]
    public void RemovedFeature_ReturnsRemovedEntries()
    {
        var left = new StudioProject
        {
            Features =
            [
                new FeatureSection { Id = "Main", Title = "Main" },
                new FeatureSection { Id = "Extra", Title = "Extra Feature" }
            ]
        };
        var right = new StudioProject
        {
            Features = [new FeatureSection { Id = "Main", Title = "Main" }]
        };

        var entries = ProjectDiffer.Diff(left, right);

        Assert.Contains(entries, e => e.Path.StartsWith("features[1]") && e.Kind == DiffKind.Removed);
        Assert.Contains(entries, e => e.Path == "features[1].id" && e.LeftValue == "Extra");
    }

    [Fact]
    public void NestedFeatureFileChanges_ReturnsCorrectPaths()
    {
        var left = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main",
                    Files = [new FileEntry { Source = "app.exe" }]
                }
            ]
        };
        var right = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main",
                    Files = [new FileEntry { Source = "app-new.exe" }]
                }
            ]
        };

        var entries = ProjectDiffer.Diff(left, right);

        var fileEntry = Assert.Single(entries, e => e.Path == "features[0].files[0].source");
        Assert.Equal(DiffKind.Modified, fileEntry.Kind);
        Assert.Equal("app.exe", fileEntry.LeftValue);
        Assert.Equal("app-new.exe", fileEntry.RightValue);
    }

    [Fact]
    public void EmptyProjects_NoDiffs()
    {
        var left = new StudioProject();
        var right = new StudioProject();

        var entries = ProjectDiffer.Diff(left, right);

        Assert.Empty(entries);
    }

    [Fact]
    public void NullArgument_ThrowsArgumentNullException()
    {
        var project = new StudioProject();

        Assert.Throws<ArgumentNullException>(() => ProjectDiffer.Diff(null!, project));
        Assert.Throws<ArgumentNullException>(() => ProjectDiffer.Diff(project, null!));
    }

    [Fact]
    public void MultipleChanges_AllDetected()
    {
        var left = CreateProject("AppA", "1.0.0");
        left.Product.Manufacturer = "OldCorp";

        var right = CreateProject("AppB", "2.0.0");
        right.Product.Manufacturer = "NewCorp";

        var entries = ProjectDiffer.Diff(left, right);

        Assert.Contains(entries, e => e.Path == "product.name" && e.Kind == DiffKind.Modified);
        Assert.Contains(entries, e => e.Path == "product.version" && e.Kind == DiffKind.Modified);
        Assert.Contains(entries, e => e.Path == "product.manufacturer" && e.Kind == DiffKind.Modified);
    }

    private static StudioProject CreateProject(string name, string version) => new()
    {
        Product = new ProductSection
        {
            Name = name,
            Version = version,
            Manufacturer = "TestCorp",
            UpgradeCode = "00000000-0000-0000-0000-000000000001"
        },
        Features = [new FeatureSection { Id = "Main", Title = "Main", IsDefault = true }]
    };
}
