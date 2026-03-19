namespace FalkForge.Studio.Graph;

public sealed class DependencyNode
{
    public string Id { get; }
    public string Label { get; }
    public DependencyNodeType NodeType { get; }
    public List<DependencyNode> Children { get; } = [];

    public DependencyNode(string id, string label, DependencyNodeType nodeType)
    {
        Id = id;
        Label = label;
        NodeType = nodeType;
    }
}
