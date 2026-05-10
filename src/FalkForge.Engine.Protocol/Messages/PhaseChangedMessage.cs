namespace FalkForge.Engine.Protocol.Messages;

public sealed class PhaseChangedMessage : EngineMessage
{
    public override MessageType Type => MessageType.PhaseChanged;
    public required EnginePhase Phase { get; init; }

    /// <summary>
    /// Identifies the install session that emitted this phase transition. Allows correlating
    /// log streams from the UI process, Engine process, and Elevation process for a
    /// single install/uninstall session. Stamped by the engine before sending.
    /// </summary>
    public Guid SessionCorrelationId { get; init; }
}
