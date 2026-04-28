using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class DictionaryStreamRegistryTests
{
    private static StreamSource MakeSource(byte[] payload)
    {
        byte[] hash = SHA256.HashData(payload);
        return new StreamSource.InMemory(payload, hash);
    }

    [Fact]
    public void Register_then_TryGet_round_trips_source()
    {
        DictionaryStreamRegistry registry = new();
        StreamSource source = MakeSource([1, 2, 3]);

        registry.Register("Stream1", source);

        Assert.True(registry.TryGet("Stream1", out StreamSource fetched));
        Assert.Same(source, fetched);
    }

    [Fact]
    public void TryGet_returns_false_for_unknown_name()
    {
        DictionaryStreamRegistry registry = new();

        Assert.False(registry.TryGet("Missing", out StreamSource fetched));
        Assert.Null(fetched);
    }

    [Fact]
    public void Register_throws_on_duplicate_stream_name()
    {
        DictionaryStreamRegistry registry = new();
        StreamSource a = MakeSource([1]);
        StreamSource b = MakeSource([2]);

        registry.Register("Dup", a);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register("Dup", b));
        Assert.Contains("Dup", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_reflects_all_registered_streams()
    {
        DictionaryStreamRegistry registry = new();
        StreamSource a = MakeSource([1]);
        StreamSource b = MakeSource([2, 3]);

        registry.Register("A", a);
        registry.Register("B", b);

        IReadOnlyDictionary<string, StreamSource> snapshot = registry.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Same(a, snapshot["A"]);
        Assert.Same(b, snapshot["B"]);
    }

    [Fact]
    public void Snapshot_does_not_observe_post_snapshot_registrations()
    {
        DictionaryStreamRegistry registry = new();
        registry.Register("A", MakeSource([1]));

        IReadOnlyDictionary<string, StreamSource> snapshot = registry.Snapshot();
        registry.Register("B", MakeSource([2]));

        Assert.Single(snapshot);
        Assert.True(snapshot.ContainsKey("A"));
        Assert.False(snapshot.ContainsKey("B"));
    }

    [Fact]
    public void Register_throws_on_null_stream_name()
    {
        DictionaryStreamRegistry registry = new();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!, MakeSource([1])));
    }

    [Fact]
    public void Register_throws_on_null_source()
    {
        DictionaryStreamRegistry registry = new();
        Assert.Throws<ArgumentNullException>(() => registry.Register("X", null!));
    }
}
