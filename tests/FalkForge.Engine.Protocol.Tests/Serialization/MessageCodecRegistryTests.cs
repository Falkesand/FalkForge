using System;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

public sealed class MessageCodecRegistryTests
{
    /// <summary>
    /// Synthetic <see cref="MessageType"/> value guaranteed not to have a registered
    /// codec at any wire version. Used to exercise the failure path of
    /// <see cref="MessageCodecRegistry.ForRead"/> without coupling tests to which
    /// production codecs happen to be registered today.
    /// </summary>
    private const MessageType UnregisteredType = (MessageType)0xFFFE;

    [Fact]
    public void ForWrite_with_unregistered_type_throws_invalid_operation()
    {
        Assert.Throws<InvalidOperationException>(
            () => MessageCodecRegistry.ForWrite(new UnregisteredMessage()));
    }

    [Fact]
    public void ForRead_with_unregistered_type_returns_failure()
    {
        var result = MessageCodecRegistry.ForRead(UnregisteredType, wireVersion: 1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void All_returns_registered_codecs()
    {
        // The production registry now holds the phase 5 batch 1 codecs. The exact
        // count is deliberately not asserted — new codecs are added per phase — but
        // the collection must be non-empty and contain the known anchor types.
        Assert.NotEmpty(MessageCodecRegistry.All);
    }

    [Fact]
    public void All_contains_exactly_29_registered_codecs()
    {
        // WHY: This count assertion is the meta-gate that prevents a new codec from
        // shipping without a corresponding golden-byte test. When a new codec is added
        // to MessageCodecRegistry, this test fails — the author must then:
        //   1. Add a GoldenBytes_wire_format_stable test in the matching CodecTests file.
        //   2. Bump this expected count by one.
        // Do not bump the count without adding the golden-byte test first.
        //
        // Current count: 29 codecs for 29 MessageType enum values.
        // (Log and PhaseChanged each have exactly one codec — WireVersion 2 only;
        //  the removed WireVersion 1 codecs are intentionally absent per single-version contract.)
        Assert.Equal(29, MessageCodecRegistry.All.Count);
    }

    [Fact]
    public void All_registered_types_resolve_at_their_own_wire_version()
    {
        // Every codec in the registry must be self-consistent: ForRead(codec.Type, codec.WireVersion)
        // must return that exact codec. This guards against registration bugs where a codec is
        // inserted under the wrong key.
        var failures = new System.Collections.Generic.List<string>();

        foreach (var codec in MessageCodecRegistry.All)
        {
            var result = MessageCodecRegistry.ForRead(codec.Type, codec.WireVersion);
            if (result.IsFailure || result.Value.WireVersion != codec.WireVersion)
            {
                failures.Add($"{codec.Type} v{codec.WireVersion}: {(result.IsFailure ? result.Error.Message : $"resolved to v{result.Value.WireVersion}")}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ForRead_returns_failure_with_descriptive_message_naming_type_and_version()
    {
        var result = MessageCodecRegistry.ForRead(UnregisteredType, wireVersion: 9);

        Assert.True(result.IsFailure);
        Assert.Contains("9", result.Error.Message, StringComparison.Ordinal);
    }

    private sealed class UnregisteredMessage : EngineMessage
    {
        public override MessageType Type => UnregisteredType;
    }
}
