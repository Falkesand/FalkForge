namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Protocol;

/// <summary>
/// Discriminated union of observable events emitted by the installer pipeline.
/// Consumers receive these via <see cref="IUiChannel.SendAsync"/> or the
/// <c>IInstallerPipeline.Events</c> observable.
/// </summary>
public abstract record PipelineEvent
{
    private PipelineEvent() { }

    /// <summary>The engine has entered a new phase.</summary>
    public sealed record PhaseChanged(EnginePhase Phase) : PipelineEvent;

    /// <summary>Overall installation progress update.</summary>
    public sealed record Progress(int Percent, string? Message) : PipelineEvent;

    /// <summary>A diagnostic log message from the engine.</summary>
    public sealed record Log(LogLevel Level, string Message) : PipelineEvent;

    /// <summary>A terminal failure has occurred.</summary>
    public sealed record Failed(ErrorKind Kind, string Message) : PipelineEvent;

    /// <summary>A single rollback step has completed.</summary>
    public sealed record RollbackStep(RollbackStepResult Step) : PipelineEvent;
}
