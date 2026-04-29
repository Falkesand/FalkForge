using System.Runtime.InteropServices;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Composite lookup key for the codec registry, pairing a protocol
/// <see cref="MessageType"/> with a specific wire-format version.
/// </summary>
/// <remarks>
/// Used as the dictionary key in <c>MessageCodecRegistry</c> so a single message type
/// can have multiple registered codecs across wire-version migrations. The struct is
/// kept <see langword="internal"/> because callers should resolve codecs through the
/// registry's typed methods rather than constructing keys directly.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CodecKey
{
    /// <summary>The protocol message type this codec handles.</summary>
    public required MessageType Type { get; init; }

    /// <summary>The wire-format version of the codec.</summary>
    public required ushort WireVersion { get; init; }
}
