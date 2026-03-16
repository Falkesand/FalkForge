using FalkForge.Studio.Graph;

namespace FalkForge.Studio.Editors.DependencyGraph;

public sealed class GraphNodeViewModel
{
    public string Id { get; }
    public string Label { get; }
    public DependencyNodeType NodeType { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsOrphaned { get; set; }

    public GraphNodeViewModel(string id, string label, DependencyNodeType nodeType)
    {
        Id = id;
        Label = label;
        NodeType = nodeType;
    }
}
