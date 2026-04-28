using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class NoOpFileSequencerTests
{
    [Fact]
    public void Sequence_returns_empty_array_for_empty_context()
    {
        RecipeBuildContext context = MakeEmptyContext();
        NoOpFileSequencer sequencer = new();

        ImmutableArray<FileSequenceEntry> result = sequencer.Sequence(context);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Sequence_returns_initialized_immutable_array()
    {
        RecipeBuildContext context = MakeEmptyContext();
        NoOpFileSequencer sequencer = new();

        ImmutableArray<FileSequenceEntry> result = sequencer.Sequence(context);

        // IsDefault must be false — caller can iterate without NRE.
        Assert.False(result.IsDefault);
    }

    [Fact]
    public void Sequence_throws_on_null_context()
    {
        NoOpFileSequencer sequencer = new();
        Assert.Throws<ArgumentNullException>(() => sequencer.Sequence(null!));
    }

    private static RecipeBuildContext MakeEmptyContext()
    {
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel { Name = "Test", Manufacturer = "M", Version = new Version(1, 0, 0) },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
        return new RecipeBuildContext(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
    }
}
