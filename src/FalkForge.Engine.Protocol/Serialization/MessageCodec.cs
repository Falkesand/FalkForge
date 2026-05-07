using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Strongly typed codec for a single <see cref="EngineMessage"/> subclass. Holds the field
/// schema and the read/write delegates. Registration sites are expected to supply
/// <c>static</c> lambdas so that no implicit closures are created (NativeAOT-friendly).
/// </summary>
/// <typeparam name="T">Concrete message type produced by this codec.</typeparam>
public sealed record MessageCodec<T> : IMessageCodec
    where T : EngineMessage
{
    /// <inheritdoc />
    public required MessageType Type { get; init; }

    /// <inheritdoc />
    public required ushort WireVersion { get; init; }

    /// <inheritdoc />
    public required ImmutableArray<FieldDescriptor> Fields { get; init; }

    /// <summary>Typed write delegate. Should serialize the body fields only.</summary>
    public required Action<BinaryWriter, T> Write { get; init; }

    /// <summary>Typed read delegate. Should deserialize the body fields only.</summary>
    public required Func<BinaryReader, T> Read { get; init; }

    /// <summary>
    /// Optional post-write hook invoked after <see cref="Write"/> completes.
    /// Used by <c>SetSecurePropertyCodec</c> to dispose the message's
    /// <see cref="SensitiveBytes"/> after the bytes have been written to the wire,
    /// ensuring plaintext does not linger in the caller's heap.
    /// Default <see langword="null"/> (no-op).
    /// </summary>
    public Action<T>? PostWrite { get; init; }

    /// <summary>
    /// Optional post-read hook invoked after <see cref="Read"/> returns.
    /// Receives and may replace the decoded message before it is returned to the caller.
    /// Default <see langword="null"/> (no-op).
    /// </summary>
    public Func<T, T>? PostRead { get; init; }

    /// <inheritdoc />
    public Type MessageClrType => typeof(T);

    /// <inheritdoc />
    public void WriteErased(BinaryWriter writer, EngineMessage message)
    {
        if (message is not T typed)
        {
            throw new ArgumentException(
                $"Codec for {typeof(T).Name} cannot write a message of type {message?.GetType().Name ?? "<null>"}.",
                nameof(message));
        }

        Write(writer, typed);
        PostWrite?.Invoke(typed);
    }

    /// <inheritdoc />
    public EngineMessage ReadErased(BinaryReader reader)
    {
        var value = Read(reader);
        if (PostRead is not null)
            value = PostRead(value);
        return value;
    }
}
