using System.IO;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Graph;

public static class DependencyGraphBuilder
{
    public static DependencyGraph Build(StudioProject project)
    {
        var graph = new DependencyGraph();
        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in project.Features)
            BuildFeatureNode(feature, graph, referencedFiles, parentId: null);

        foreach (var service in project.Services)
        {
            var serviceId = $"service:{service.Name}";
            var node = new DependencyNode(serviceId, service.DisplayName, DependencyNodeType.Service);
            graph.Nodes.Add(node);
        }

        foreach (var reg in project.Registry)
        {
            var regId = $"registry:{reg.Root}\\{reg.Key}\\{reg.ValueName}";
            var node = new DependencyNode(regId, $"{reg.Root}\\{reg.Key}\\{reg.ValueName}", DependencyNodeType.Registry);
            graph.Nodes.Add(node);
        }

        foreach (var shortcut in project.Shortcuts)
        {
            var shortcutId = $"shortcut:{shortcut.Name}";
            var node = new DependencyNode(shortcutId, shortcut.Name, DependencyNodeType.Shortcut);
            graph.Nodes.Add(node);
        }

        // Detect orphaned files: collect all files across all features, find those not referenced
        var allProjectFiles = CollectAllFiles(project);
        foreach (var file in allProjectFiles)
        {
            if (!referencedFiles.Contains(file))
                graph.OrphanedFiles.Add(file);
        }

        return graph;
    }

    private static void BuildFeatureNode(
        FeatureSection feature,
        DependencyGraph graph,
        HashSet<string> referencedFiles,
        string? parentId)
    {
        var featureId = $"feature:{feature.Id}";
        var featureNode = new DependencyNode(featureId, feature.Title, DependencyNodeType.Feature);
        graph.Nodes.Add(featureNode);

        if (parentId is not null)
            graph.Edges.Add(new DependencyEdge(parentId, featureId, "contains"));

        foreach (var file in feature.Files)
        {
            var fileName = Path.GetFileName(file.Source);
            var fileId = $"file:{file.Source}";
            var fileNode = new DependencyNode(fileId, fileName, DependencyNodeType.File);
            featureNode.Children.Add(fileNode);
            graph.Nodes.Add(fileNode);
            graph.Edges.Add(new DependencyEdge(featureId, fileId, "contains"));
            referencedFiles.Add(file.Source);
        }

        if (feature.Features is not null)
        {
            foreach (var sub in feature.Features)
                BuildFeatureNode(sub, graph, referencedFiles, featureId);
        }
    }

    private static HashSet<string> CollectAllFiles(StudioProject project)
    {
        // Files referenced in features are already tracked via referencedFiles.
        // "Orphaned" files are those that appear as standalone file entries
        // in any feature but are duplicated or referenced in services/shortcuts
        // without being in any feature. For simplicity, we consider a file orphaned
        // if it appears in Services.Executable or Shortcuts.TargetFile but not in any feature.
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var service in project.Services)
        {
            if (!string.IsNullOrEmpty(service.Executable))
                files.Add(service.Executable);
        }

        foreach (var shortcut in project.Shortcuts)
        {
            if (!string.IsNullOrEmpty(shortcut.TargetFile))
                files.Add(shortcut.TargetFile);
        }

        return files;
    }
}
