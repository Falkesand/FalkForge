namespace FalkForge.Engine.Execution;

public sealed class ExecutionOutcome
{
    public required ExitCodeBehavior Behavior { get; init; }

    public static ExecutionOutcome Success => new() { Behavior = ExitCodeBehavior.Success };
    public static ExecutionOutcome RebootRequired => new() { Behavior = ExitCodeBehavior.RebootRequired };
    public static ExecutionOutcome ScheduleReboot => new() { Behavior = ExitCodeBehavior.ScheduleReboot };
}
