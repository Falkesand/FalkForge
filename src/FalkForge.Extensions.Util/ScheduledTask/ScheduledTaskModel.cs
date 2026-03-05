namespace FalkForge.Extensions.Util.ScheduledTask;

public sealed class ScheduledTaskModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public required ScheduledTaskTriggerType TriggerType { get; init; }
    public string? Schedule { get; init; }
    public string? RunAsUser { get; init; }
    public bool RunElevated { get; init; }
}
