using System.Collections.Immutable;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Version negotiation tests: verifies that <see cref="MessageCodecRegistryInstance"/>
/// resolves exact version matches, falls back to the nearest lower version when no
/// exact match exists, and rejects entirely unknown types.
/// </summary>
public class VersionNegotiationTests
{
    // --- Exact version match ---

    [Fact]
    public void ForRead_exact_version_match_returns_correct_codec()
    {
        var codec = new MessageCodec<CancelMessage>
        {
            Type = MessageType.Cancel,
            WireVersion = 7,
            Fields = ImmutableArray<FieldDescriptor>.Empty,
            Write = static (_, _) => { },
            Read = static _ => new CancelMessage { SequenceId = 0 },
        };

        var registry = new MessageCodecRegistryInstance([codec]);
        var result = registry.ForRead(MessageType.Cancel, 7);

        Assert.True(result.IsSuccess);
        Assert.Same(codec, result.Value);
    }

    // --- Version fallback ---

    [Fact]
    public void ForRead_falls_back_to_nearest_lower_version()
    {
        var v1 = new MessageCodec<CancelMessage>
        {
            Type = MessageType.Cancel,
            WireVersion = 1,
            Fields = ImmutableArray<FieldDescriptor>.Empty,
            Write = static (_, _) => { },
            Read = static _ => new CancelMessage { SequenceId = 0 },
        };
        var v3 = new MessageCodec<CancelMessage>
        {
            Type = MessageType.Cancel,
            WireVersion = 3,
            Fields = ImmutableArray<FieldDescriptor>.Empty,
            Write = static (_, _) => { },
            Read = static _ => new CancelMessage { SequenceId = 0 },
        };

        var registry = new MessageCodecRegistryInstance([v1, v3]);

        // Wire version 5 not registered — falls back to v3 (nearest lower).
        var result = registry.ForRead(MessageType.Cancel, 5);

        Assert.True(result.IsSuccess);
        Assert.Same(v3, result.Value);
    }

    [Fact]
    public void ForRead_falls_back_to_v1_when_only_v1_registered_and_higher_requested()
    {
        var v1 = new MessageCodec<ProgressMessage>
        {
            Type = MessageType.Progress,
            WireVersion = 1,
            Fields = ImmutableArray<FieldDescriptor>.Empty,
            Write = static (_, _) => { },
            Read = static _ => new ProgressMessage { SequenceId = 0, Progress = new InstallProgress(0, 0, "", 0) },
        };

        var registry = new MessageCodecRegistryInstance([v1]);

        var result = registry.ForRead(MessageType.Progress, 99);

        Assert.True(result.IsSuccess);
        Assert.Same(v1, result.Value);
    }

    // --- No codec found ---

    [Fact]
    public void ForRead_unknown_type_returns_failure()
    {
        var registry = new MessageCodecRegistryInstance([]); // empty registry

        var result = registry.ForRead((MessageType)0xFFFF, 1);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    [Fact]
    public void ForRead_known_type_but_version_lower_than_all_registered_returns_failure()
    {
        var v5 = new MessageCodec<CancelMessage>
        {
            Type = MessageType.Cancel,
            WireVersion = 5,
            Fields = ImmutableArray<FieldDescriptor>.Empty,
            Write = static (_, _) => { },
            Read = static _ => new CancelMessage { SequenceId = 0 },
        };

        var registry = new MessageCodecRegistryInstance([v5]);

        // Request v1 — only v5 registered, no lower version to fall back to.
        var result = registry.ForRead(MessageType.Cancel, 1);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    // --- Global registry has all current-wire codecs ---

    [Fact]
    public void GlobalRegistry_resolves_all_29_message_types_at_their_minimum_wire_version()
    {
        // Log and PhaseChanged were promoted to WireVersion 2 to carry SessionCorrelationId.
        // All other types remain at WireVersion 1. Verify each type resolves at its own
        // minimum supported version (not necessarily 1).
        var v2Types = new HashSet<MessageType>
        {
            MessageType.Log,
            MessageType.PhaseChanged,
        };

        // MessageType.None (C10) is a zero-value sentinel, not a real wire message — it has
        // no codec and is excluded from this "every real wire type resolves" check.
        var types = Enum.GetValues<MessageType>().Where(t => t != MessageType.None);
        var failures = new List<string>();

        foreach (var type in types)
        {
            var minVersion = v2Types.Contains(type) ? (ushort)2 : (ushort)1;
            var result = MessageCodecRegistry.ForRead(type, minVersion);
            if (result.IsFailure)
                failures.Add($"{type} (v{minVersion})");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void GlobalRegistry_Log_and_PhaseChanged_not_resolvable_at_v1()
    {
        // WHY: Log and PhaseChanged were promoted to WireVersion 2. A peer that only
        // knows WireVersion 1 for these types must fail fast rather than silently drop
        // the SessionCorrelationId field. Single-version contract means we do not need
        // to support legacy v1 frames for these two types.
        Assert.True(MessageCodecRegistry.ForRead(MessageType.Log, 1).IsFailure);
        Assert.True(MessageCodecRegistry.ForRead(MessageType.PhaseChanged, 1).IsFailure);
    }
}
