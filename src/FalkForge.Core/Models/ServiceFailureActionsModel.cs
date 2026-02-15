namespace FalkForge.Models;

public sealed class ServiceFailureActionsModel
{
    public FailureAction OnFirstFailure { get; init; } = FailureAction.None;
    public FailureAction OnSecondFailure { get; init; } = FailureAction.None;
    public FailureAction OnSubsequentFailures { get; init; } = FailureAction.None;
    public TimeSpan ResetPeriod { get; init; } = TimeSpan.FromDays(1);
    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromMinutes(1);
    public string? Command { get; init; }
    public string? RebootMessage { get; init; }
}
