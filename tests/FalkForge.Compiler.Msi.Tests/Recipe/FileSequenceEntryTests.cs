using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class FileSequenceEntryTests
{
    [Fact]
    public void Construct_preserves_file_id_and_sequence()
    {
        FileSequenceEntry entry = new("MyFile.exe", 1);

        Assert.Equal("MyFile.exe", entry.FileId);
        Assert.Equal(1, entry.Sequence);
    }

    [Fact]
    public void Equal_entries_compare_equal()
    {
        FileSequenceEntry a = new("X", 5);
        FileSequenceEntry b = new("X", 5);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_sequence_not_equal()
    {
        FileSequenceEntry a = new("X", 1);
        FileSequenceEntry b = new("X", 2);

        Assert.NotEqual(a, b);
    }
}
