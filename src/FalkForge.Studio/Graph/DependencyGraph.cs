namespace FalkForge.Studio.Graph;

public sealed class DependencyGraph
{
    public List<DependencyNode> Nodes { get; } = [];
    public List<DependencyEdge> Edges { get; } = [];
    public List<string> OrphanedFiles { get; } = [];
}
