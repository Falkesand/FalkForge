using FalkForge.Studio.Graph;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Graph;

public class DependencyGraphBuilderTests
{
    [Fact]
    public void Build_EmptyProject_ReturnsEmptyGraph()
    {
        var project = new StudioProject();

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Empty(graph.OrphanedFiles);
    }

    [Fact]
    public void Build_SingleFeatureWithFiles_CreatesCorrectNodesAndEdges()
    {
        var project = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main Feature",
                    Files =
                    [
                        new FileEntry { Source = "app.exe" },
                        new FileEntry { Source = "config.json" }
                    ]
                }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Equal(3, graph.Nodes.Count); // 1 feature + 2 files
        Assert.Equal(2, graph.Edges.Count); // feature -> each file
        Assert.All(graph.Edges, e => Assert.Equal("contains", e.EdgeType));
        Assert.Contains(graph.Nodes, n => n.Id == "feature:Main" && n.NodeType == DependencyNodeType.Feature);
        Assert.Contains(graph.Nodes, n => n.Id == "file:app.exe" && n.NodeType == DependencyNodeType.File);
        Assert.Contains(graph.Nodes, n => n.Id == "file:config.json" && n.NodeType == DependencyNodeType.File);
    }

    [Fact]
    public void Build_NestedFeatures_CreatesHierarchyEdges()
    {
        var project = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Parent",
                    Title = "Parent",
                    Features =
                    [
                        new FeatureSection
                        {
                            Id = "Child",
                            Title = "Child",
                            Files = [new FileEntry { Source = "child.dll" }]
                        }
                    ]
                }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Equal(3, graph.Nodes.Count); // Parent + Child + file
        Assert.Contains(graph.Edges, e => e.FromId == "feature:Parent" && e.ToId == "feature:Child" && e.EdgeType == "contains");
        Assert.Contains(graph.Edges, e => e.FromId == "feature:Child" && e.ToId == "file:child.dll" && e.EdgeType == "contains");
    }

    [Fact]
    public void Build_OrphanedFile_DetectedWhenServiceExeNotInFeature()
    {
        var project = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main",
                    Files = [new FileEntry { Source = "app.exe" }]
                }
            ],
            Services =
            [
                new ServiceSection { Name = "svc", DisplayName = "Service", Executable = "service.exe" }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Single(graph.OrphanedFiles);
        Assert.Contains("service.exe", graph.OrphanedFiles);
    }

    [Fact]
    public void Build_ServiceExeInFeature_NotOrphaned()
    {
        var project = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main",
                    Files = [new FileEntry { Source = "service.exe" }]
                }
            ],
            Services =
            [
                new ServiceSection { Name = "svc", DisplayName = "Service", Executable = "service.exe" }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Empty(graph.OrphanedFiles);
    }

    [Fact]
    public void Build_ServicesAppearAsNodes()
    {
        var project = new StudioProject
        {
            Services =
            [
                new ServiceSection { Name = "MyService", DisplayName = "My Service" }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Contains(graph.Nodes, n => n.Id == "service:MyService" && n.NodeType == DependencyNodeType.Service);
    }

    [Fact]
    public void Build_RegistryEntriesAppearAsNodes()
    {
        var project = new StudioProject
        {
            Registry =
            [
                new RegistryEntrySection { Root = "LocalMachine", Key = @"SOFTWARE\Test", ValueName = "Val" }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Contains(graph.Nodes, n => n.NodeType == DependencyNodeType.Registry);
    }

    [Fact]
    public void Build_ShortcutsAppearAsNodes()
    {
        var project = new StudioProject
        {
            Shortcuts =
            [
                new ShortcutSection { Name = "MyApp", TargetFile = "app.exe" }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Contains(graph.Nodes, n => n.Id == "shortcut:MyApp" && n.NodeType == DependencyNodeType.Shortcut);
    }

    [Fact]
    public void Build_MultipleFeaturesWithSharedReferences_AllNodesCreated()
    {
        var project = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Feature1",
                    Title = "Feature One",
                    Files = [new FileEntry { Source = "shared.dll" }]
                },
                new FeatureSection
                {
                    Id = "Feature2",
                    Title = "Feature Two",
                    Files = [new FileEntry { Source = "unique.dll" }]
                }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Equal(4, graph.Nodes.Count); // 2 features + 2 files
        Assert.Equal(2, graph.Edges.Count);
    }

    [Fact]
    public void Build_ShortcutTargetNotInFeature_Orphaned()
    {
        var project = new StudioProject
        {
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main",
                    Files = [new FileEntry { Source = "app.exe" }]
                }
            ],
            Shortcuts =
            [
                new ShortcutSection { Name = "Link", TargetFile = "other.exe" }
            ]
        };

        var graph = DependencyGraphBuilder.Build(project);

        Assert.Single(graph.OrphanedFiles);
        Assert.Contains("other.exe", graph.OrphanedFiles);
    }
}
