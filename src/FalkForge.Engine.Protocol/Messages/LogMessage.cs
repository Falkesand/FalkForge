namespace FalkForge.Engine.Protocol.Messages;

public sealed class LogMessage : EngineMessage
{
    public override MessageType Type => MessageType.Log;
    public required string Text { get; init; }
    public required LogLevel Level { get; init; }

    /// <summary>
    /// Identifies the install session that emitted this log entry. Allows correlating
    /// log streams from the UI process, Engine process, and Elevation process for a
    /// single install/uninstall session. Stamped by the engine before sending.
    /// </summary>
    public Guid SessionCorrelationId { get; init; }
}
