namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class ServiceFailureActionsBuilder
{
    public FailureAction OnFirstFailure { get; set; } = FailureAction.None;
    public FailureAction OnSecondFailure { get; set; } = FailureAction.None;
    public FailureAction OnSubsequentFailures { get; set; } = FailureAction.None;
    public TimeSpan ResetPeriod { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromMinutes(1);
    public string? Command { get; set; }
    public string? RebootMessage { get; set; }

    internal ServiceFailureActionsModel Build() => new()
    {
        OnFirstFailure = OnFirstFailure,
        OnSecondFailure = OnSecondFailure,
        OnSubsequentFailures = OnSubsequentFailures,
        ResetPeriod = ResetPeriod,
        RestartDelay = RestartDelay,
        Command = Command,
        RebootMessage = RebootMessage
    };
}
