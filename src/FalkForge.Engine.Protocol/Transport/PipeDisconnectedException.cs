namespace FalkForge.Engine.Protocol.Transport;

/// <summary>
/// Thrown when the named pipe transport closes unexpectedly while the caller
/// is awaiting a response (Detect, Plan, or Apply).
/// </summary>
public sealed class PipeDisconnectedException : Exception
{
    public PipeDisconnectedException()
        : base("The engine pipe closed unexpectedly while awaiting a response.")
    {
    }

    public PipeDisconnectedException(string message)
        : base(message)
    {
    }

    public PipeDisconnectedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
