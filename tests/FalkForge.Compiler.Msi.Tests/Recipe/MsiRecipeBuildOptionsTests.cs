using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class MsiRecipeBuildOptionsTests
{
    [Fact]
    public void Default_construction_has_expected_defaults()
    {
        MsiRecipeBuildOptions options = new();

        Assert.Equal(FileSequencingStrategy.FileIdOrdinal, options.Sequencing);
        Assert.True(options.EagerStreamHashing);
        Assert.Equal(256 * 1024, options.MaxInMemoryStreamBytes);
    }

    [Fact]
    public void With_expression_overrides_sequencing()
    {
        MsiRecipeBuildOptions options = new() { Sequencing = FileSequencingStrategy.ComponentThenFileId };

        Assert.Equal(FileSequencingStrategy.ComponentThenFileId, options.Sequencing);
    }

    [Fact]
    public void With_expression_overrides_eager_hashing()
    {
        MsiRecipeBuildOptions options = new() { EagerStreamHashing = false };

        Assert.False(options.EagerStreamHashing);
    }

    [Fact]
    public void With_expression_overrides_max_in_memory_bytes()
    {
        MsiRecipeBuildOptions options = new() { MaxInMemoryStreamBytes = 1024 };

        Assert.Equal(1024, options.MaxInMemoryStreamBytes);
    }

    [Fact]
    public void Record_equality_holds_for_default_instances()
    {
        MsiRecipeBuildOptions a = new();
        MsiRecipeBuildOptions b = new();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Record_equality_distinguishes_different_options()
    {
        MsiRecipeBuildOptions a = new();
        MsiRecipeBuildOptions b = new() { MaxInMemoryStreamBytes = 1 };

        Assert.NotEqual(a, b);
    }
}
