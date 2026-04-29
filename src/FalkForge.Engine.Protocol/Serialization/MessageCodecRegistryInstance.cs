using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Instance form of the codec registry. Holds frozen lookup tables keyed by
/// <see cref="CodecKey"/> and by CLR <see cref="System.Type"/> so write-side resolution
/// (CLR type known) and read-side resolution (wire-format type and version known) are both
/// O(1) in the common case.
/// </summary>
/// <remarks>
/// The static <c>MessageCodecRegistry</c> facade delegates to a singleton of this class.
/// Tests construct their own instances with stub codecs, which keeps the production registry
/// immutable while still exercising the version-fallback and duplicate-detection paths.
/// </remarks>
internal sealed class MessageCodecRegistryInstance
{
    private readonly FrozenDictionary<CodecKey, IMessageCodec> _byKey;
    private readonly FrozenDictionary<Type, IMessageCodec> _byClrType;

    /// <summary>
    /// Builds a new registry from the supplied codec set. Throws when two codecs share the
    /// same <see cref="MessageType"/>+<see cref="IMessageCodec.WireVersion"/> pair or the
    /// same CLR <see cref="System.Type"/>.
    /// </summary>
    /// <param name="codecs">The codec set to register. May be empty.</param>
    /// <exception cref="InvalidOperationException">A duplicate registration was detected.</exception>
    public MessageCodecRegistryInstance(IMessageCodec[] codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);

        var keyBuilder = new Dictionary<CodecKey, IMessageCodec>(codecs.Length);
        var clrBuilder = new Dictionary<Type, IMessageCodec>(codecs.Length);

        foreach (var codec in codecs)
        {
            var key = new CodecKey { Type = codec.Type, WireVersion = codec.WireVersion };
            if (!keyBuilder.TryAdd(key, codec))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "Duplicate codec registration for message type {0} wire version {1}.",
                    codec.Type,
                    codec.WireVersion));
            }

            if (!clrBuilder.TryAdd(codec.MessageClrType, codec))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "Duplicate codec registration for CLR type {0}.",
                    codec.MessageClrType.FullName ?? codec.MessageClrType.Name));
            }
        }

        _byKey = keyBuilder.ToFrozenDictionary();
        _byClrType = clrBuilder.ToFrozenDictionary();
    }

    /// <summary>All registered codecs, keyed by <see cref="CodecKey"/> in the underlying table.</summary>
    public IReadOnlyCollection<IMessageCodec> All => _byKey.Values;

    /// <summary>
    /// Resolves the codec for serializing <paramref name="message"/> by its CLR type.
    /// </summary>
    /// <exception cref="InvalidOperationException">No codec is registered for the message's runtime type.</exception>
    public IMessageCodec ForWrite(EngineMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var clrType = message.GetType();
        if (_byClrType.TryGetValue(clrType, out var codec))
        {
            return codec;
        }

        throw new InvalidOperationException(string.Format(
            CultureInfo.InvariantCulture,
            "No codec registered for CLR message type {0}.",
            clrType.FullName ?? clrType.Name));
    }

    /// <summary>
    /// Resolves the codec for deserializing a message of <paramref name="type"/> at
    /// <paramref name="wireVersion"/>. Falls back to the highest registered version
    /// less than or equal to <paramref name="wireVersion"/> for the same message type
    /// when no exact match exists, supporting forward-compatible reads.
    /// </summary>
    public Result<IMessageCodec> ForRead(MessageType type, ushort wireVersion)
    {
        var exactKey = new CodecKey { Type = type, WireVersion = wireVersion };
        if (_byKey.TryGetValue(exactKey, out var exact))
        {
            return Result<IMessageCodec>.Success(exact);
        }

        IMessageCodec? best = null;
        foreach (var pair in _byKey)
        {
            if (pair.Key.Type != type)
            {
                continue;
            }

            if (pair.Key.WireVersion > wireVersion)
            {
                continue;
            }

            if (best is null || pair.Key.WireVersion > best.WireVersion)
            {
                best = pair.Value;
            }
        }

        if (best is not null)
        {
            return Result<IMessageCodec>.Success(best);
        }

        return Result<IMessageCodec>.Failure(
            ErrorKind.Validation,
            string.Format(
                CultureInfo.InvariantCulture,
                "No codec for {0} v{1}",
                type,
                wireVersion));
    }
}
