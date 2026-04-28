using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class FileSequencingStrategyTests
{
    [Fact]
    public void FileIdOrdinal_is_declared_with_zero_value()
    {
        Assert.Equal(0, (int)FileSequencingStrategy.FileIdOrdinal);
    }

    [Fact]
    public void ComponentThenFileId_is_declared_with_one_value()
    {
        Assert.Equal(1, (int)FileSequencingStrategy.ComponentThenFileId);
    }

    [Fact]
    public void Both_strategy_values_are_distinct()
    {
        Assert.NotEqual(FileSequencingStrategy.FileIdOrdinal, FileSequencingStrategy.ComponentThenFileId);
    }
}
