namespace FalkForge.Studio.Graph;

public sealed record DependencyEdge(string FromId, string ToId, string EdgeType);
