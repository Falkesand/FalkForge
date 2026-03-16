namespace FalkForge.Studio.Project;

public sealed class ProjectTemplate
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Func<StudioProject> Create { get; init; }
}
