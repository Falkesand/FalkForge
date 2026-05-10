using System.Collections.ObjectModel;
using System.Windows.Input;
using FalkForge.Studio.Graph;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.DependencyGraph;

public sealed class DependencyGraphViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private string _statusText = "";

    public ObservableCollection<GraphNodeViewModel> GraphNodes { get; } = [];
    public ObservableCollection<GraphEdgeViewModel> GraphEdges { get; } = [];
    public ObservableCollection<string> OrphanedFiles { get; } = [];
    public ICommand RefreshCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// Fired when the user clicks a node. The string is the editor key to navigate to.
    /// </summary>
    public event EventHandler<string>? NavigateRequested;

    public DependencyGraphViewModel(StudioProject project)
    {
        _project = project;
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public void Refresh()
    {
        GraphNodes.Clear();
        GraphEdges.Clear();
        OrphanedFiles.Clear();

        var graph = DependencyGraphBuilder.Build(_project);

        // Layout: hierarchical by type rows
        var featureNodes = new List<GraphNodeViewModel>();
        var fileNodes = new List<GraphNodeViewModel>();
        var otherNodes = new List<GraphNodeViewModel>();

        foreach (var node in graph.Nodes)
        {
            var vm = new GraphNodeViewModel(node.Id, node.Label, node.NodeType);
            switch (node.NodeType)
            {
                case DependencyNodeType.Feature:
                    featureNodes.Add(vm);
                    break;
                case DependencyNodeType.File:
                    fileNodes.Add(vm);
                    break;
                default:
                    otherNodes.Add(vm);
                    break;
            }
            GraphNodes.Add(vm);
        }

        // Mark orphaned file nodes
        foreach (var orphan in graph.OrphanedFiles)
            OrphanedFiles.Add(orphan);

        // Simple hierarchical layout
        const double nodeWidth = 140;
        const double rowSpacing = 120;
        const double startX = 20;
        const double startY = 20;

        LayoutRow(featureNodes, startX, startY, nodeWidth);
        LayoutRow(fileNodes, startX, startY + rowSpacing, nodeWidth);
        LayoutRow(otherNodes, startX, startY + rowSpacing * 2, nodeWidth);

        // Build lookup for edge drawing
        var nodeMap = new Dictionary<string, GraphNodeViewModel>();
        foreach (var n in GraphNodes)
            nodeMap[n.Id] = n;

        const double nodeHalfWidth = 70;
        const double nodeHeight = 30;

        foreach (var edge in graph.Edges)
        {
            if (nodeMap.TryGetValue(edge.FromId, out var from) &&
                nodeMap.TryGetValue(edge.ToId, out var to))
            {
                GraphEdges.Add(new GraphEdgeViewModel
                {
                    X1 = from.X + nodeHalfWidth,
                    Y1 = from.Y + nodeHeight,
                    X2 = to.X + nodeHalfWidth,
                    Y2 = to.Y,
                    EdgeType = edge.EdgeType
                });
            }
        }

        StatusText = $"{graph.Nodes.Count} nodes, {graph.Edges.Count} edges, {graph.OrphanedFiles.Count} orphaned files";
    }

    public void OnNodeClicked(GraphNodeViewModel node)
    {
        var editorKey = node.NodeType switch
        {
            DependencyNodeType.Feature => "features",
            DependencyNodeType.File => "files",
            DependencyNodeType.Service => "services",
            DependencyNodeType.Registry => "registry",
            DependencyNodeType.Shortcut => "shortcuts",
            _ => null
        };

        if (editorKey is not null)
            NavigateRequested?.Invoke(this, editorKey);
    }

    private static void LayoutRow(List<GraphNodeViewModel> nodes, double startX, double y, double spacing)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].X = startX + i * spacing;
            nodes[i].Y = y;
        }
    }
}
