using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class RecipeBuildContextTests
{
    [Fact]
    public void Constructor_assigns_all_five_dependencies()
    {
        ResolvedPackage resolved = MakeResolvedPackage();
        MsiRecipeBuildOptions options = new();
        NoOpFileSequencer sequencer = new();
        DictionaryStreamRegistry registry = new();

        RecipeBuildContext context = new(resolved, options, sequencer, registry);

        Assert.Same(resolved, context.Resolved);
        Assert.Same(options, context.Options);
        Assert.Same(sequencer, context.FileSequencer);
        Assert.Same(registry, context.Streams);
    }

    [Fact]
    public void BuiltTables_is_empty_initially()
    {
        RecipeBuildContext context = MakeContext();

        Assert.Empty(context.BuiltTables);
    }

    [Fact]
    public void AddBuiltTable_makes_table_visible_in_BuiltTables()
    {
        RecipeBuildContext context = MakeContext();
        TableId id = TableId.Create("Property").Value;
        ImmutableArray<RecipeRow> rows = ImmutableArray<RecipeRow>.Empty;

        context.AddBuiltTable(id, rows);

        Assert.True(context.BuiltTables.ContainsKey(id));
        Assert.Equal(rows, context.BuiltTables[id]);
    }

    [Fact]
    public void AddBuiltTable_throws_on_duplicate_id()
    {
        RecipeBuildContext context = MakeContext();
        TableId id = TableId.Create("Component").Value;
        context.AddBuiltTable(id, ImmutableArray<RecipeRow>.Empty);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => context.AddBuiltTable(id, ImmutableArray<RecipeRow>.Empty));
        Assert.Contains("Component", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltTables_reflects_multiple_added_tables()
    {
        RecipeBuildContext context = MakeContext();
        TableId a = TableId.Create("Property").Value;
        TableId b = TableId.Create("Component").Value;

        context.AddBuiltTable(a, ImmutableArray<RecipeRow>.Empty);
        context.AddBuiltTable(b, ImmutableArray<RecipeRow>.Empty);

        Assert.Equal(2, context.BuiltTables.Count);
        Assert.True(context.BuiltTables.ContainsKey(a));
        Assert.True(context.BuiltTables.ContainsKey(b));
    }

    [Fact]
    public void Constructor_throws_on_null_resolved()
    {
        Assert.Throws<ArgumentNullException>(() => new RecipeBuildContext(
            null!, new MsiRecipeBuildOptions(), new NoOpFileSequencer(), new DictionaryStreamRegistry()));
    }

    [Fact]
    public void Constructor_throws_on_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => new RecipeBuildContext(
            MakeResolvedPackage(), null!, new NoOpFileSequencer(), new DictionaryStreamRegistry()));
    }

    [Fact]
    public void Constructor_throws_on_null_sequencer()
    {
        Assert.Throws<ArgumentNullException>(() => new RecipeBuildContext(
            MakeResolvedPackage(), new MsiRecipeBuildOptions(), null!, new DictionaryStreamRegistry()));
    }

    [Fact]
    public void Constructor_throws_on_null_streams()
    {
        Assert.Throws<ArgumentNullException>(() => new RecipeBuildContext(
            MakeResolvedPackage(), new MsiRecipeBuildOptions(), new NoOpFileSequencer(), null!));
    }

    private static RecipeBuildContext MakeContext()
    {
        return new RecipeBuildContext(
            MakeResolvedPackage(),
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
    }

    private static ResolvedPackage MakeResolvedPackage()
    {
        return new ResolvedPackage
        {
            Package = new PackageModel { Name = "Test", Manufacturer = "M", Version = new Version(1, 0, 0) },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
