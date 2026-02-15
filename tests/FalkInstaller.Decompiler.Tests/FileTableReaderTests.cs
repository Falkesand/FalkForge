using FalkInstaller.Decompiler.TableReaders;
using Xunit;

namespace FalkInstaller.Decompiler.Tests;

public sealed class FileTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("File", []);

        var result = FileTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = FileTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_ParsesFileRows()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("File",
            [
                // File, Component_, FileName, FileSize, Version, Language, Attributes, Sequence
                ["file1", "comp1", "short.tx|myfile.txt", "1024", null, null, "0", "1"]
            ]);

        var result = FileTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("file1", result.Value[0].FileKey);
        Assert.Equal("comp1", result.Value[0].ComponentRef);
        Assert.Equal("myfile.txt", result.Value[0].FileName);
        Assert.Equal(1024, result.Value[0].FileSize);
        Assert.Equal(1, result.Value[0].Sequence);
    }

    [Fact]
    public void ParseLongFileName_ShortPipeLong_ReturnsLong()
    {
        var result = FileTableReader.ParseLongFileName("SHORT~1|LongFileName.txt");
        Assert.Equal("LongFileName.txt", result);
    }

    [Fact]
    public void ParseLongFileName_NoSeparator_ReturnsAsIs()
    {
        var result = FileTableReader.ParseLongFileName("simple.txt");
        Assert.Equal("simple.txt", result);
    }

    [Fact]
    public void ParseLongFileName_Empty_ReturnsEmpty()
    {
        var result = FileTableReader.ParseLongFileName("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Read_MultipleFiles_AllParsed()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("File",
            [
                ["file1", "comp1", "app.exe", "2048", "1.0.0", null, "0", "1"],
                ["file2", "comp1", "readme.txt", "512", null, null, "0", "2"],
                ["file3", "comp2", "config.xml", "256", null, null, "0", "3"]
            ]);

        var result = FileTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal("app.exe", result.Value[0].FileName);
        Assert.Equal("readme.txt", result.Value[1].FileName);
        Assert.Equal("config.xml", result.Value[2].FileName);
    }
}
