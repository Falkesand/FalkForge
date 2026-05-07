namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Carries a sensitive property value across the UI-to-engine pipe boundary.
/// <see cref="SecureValue"/> is a <see cref="SensitiveBytes"/> instance that zeroes
/// its backing buffer on dispose. The message is <see cref="IDisposable"/> so callers
/// can deterministically clean up plaintext. The codec's PostWrite hook disposes the
/// value automatically after serialization so one-shot messages do not leak even if
/// the caller forgets to wrap in a <c>using</c>.
/// </summary>
public sealed class SetSecurePropertyMessage : EngineMessage, IDisposable
{
    public override MessageType Type => MessageType.SetSecureProperty;
    public required string PropertyName { get; init; }
    public required SensitiveBytes SecureValue { get; init; }

    public void Dispose() => SecureValue.Dispose();
}
