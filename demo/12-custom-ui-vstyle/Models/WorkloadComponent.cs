namespace CustomUiVsStyle.Models;

public sealed class WorkloadComponent
{
    public required string Name { get; init; }
    public required string Size { get; init; }
    public required bool IsRequired { get; init; }
}