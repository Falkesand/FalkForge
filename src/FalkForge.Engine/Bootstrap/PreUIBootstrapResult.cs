namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Result of <see cref="PreUIBootstrapOrchestrator.RunAsync"/>: the outcome
/// <c>BootstrapperRunner.RunAsync</c> should act on, plus (for the unelevated
/// missing-prerequisites case) the display names of what is missing.
/// </summary>
/// <param name="Outcome">The decision the caller should act on.</param>
/// <param name="MissingPrerequisiteNames">
/// Display names of prerequisites that are missing and could not be installed because the
/// process is not elevated. Empty for every other outcome.
/// </param>
public readonly record struct PreUIBootstrapResult(
    PreUIBootstrapOutcome Outcome,
    IReadOnlyList<string> MissingPrerequisiteNames)
{
    /// <summary>Wraps an outcome that carries no missing-prerequisite names.</summary>
    public static PreUIBootstrapResult From(PreUIBootstrapOutcome outcome)
        => new(outcome, []);
}
