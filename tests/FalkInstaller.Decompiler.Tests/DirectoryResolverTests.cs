using FalkInstaller.Decompiler.TableReaders;
using Xunit;

namespace FalkInstaller.Decompiler.Tests;

public sealed class DirectoryResolverTests
{
    [Fact]
    public void Resolve_StandardDirectory_ReturnsDisplayName()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "ProgramFilesFolder", DefaultDir = "." }
        ]);

        Assert.Equal("ProgramFiles", resolver.Resolve("ProgramFilesFolder"));
    }

    [Fact]
    public void Resolve_TargetDir_ReturnsSourceDir()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "TARGETDIR", DefaultDir = "SourceDir" }
        ]);

        Assert.Equal("SourceDir", resolver.Resolve("TARGETDIR"));
    }

    [Fact]
    public void Resolve_NestedDirectory()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "TARGETDIR", DefaultDir = "SourceDir" },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "ProgramFilesFolder", ParentDirectoryId = "TARGETDIR", DefaultDir = "." },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "INSTALLFOLDER", ParentDirectoryId = "ProgramFilesFolder", DefaultDir = "MyApp" }
        ]);

        Assert.Equal("ProgramFiles/MyApp", resolver.Resolve("INSTALLFOLDER"));
    }

    [Fact]
    public void Resolve_DeeplyNestedDirectory()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "TARGETDIR", DefaultDir = "SourceDir" },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "ProgramFilesFolder", ParentDirectoryId = "TARGETDIR", DefaultDir = "." },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "ManufacturerFolder", ParentDirectoryId = "ProgramFilesFolder", DefaultDir = "Contoso" },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "INSTALLFOLDER", ParentDirectoryId = "ManufacturerFolder", DefaultDir = "MyApp" }
        ]);

        Assert.Equal("ProgramFiles/Contoso/MyApp", resolver.Resolve("INSTALLFOLDER"));
    }

    [Fact]
    public void Resolve_ShortLongFormat_UsesLongName()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "ProgramFilesFolder", DefaultDir = "." },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "INSTALLFOLDER", ParentDirectoryId = "ProgramFilesFolder", DefaultDir = "MYAPP~1|My Application" }
        ]);

        Assert.Equal("ProgramFiles/My Application", resolver.Resolve("INSTALLFOLDER"));
    }

    [Fact]
    public void Resolve_CachesResults()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "ProgramFilesFolder", DefaultDir = "." },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "INSTALLFOLDER", ParentDirectoryId = "ProgramFilesFolder", DefaultDir = "MyApp" }
        ]);

        var first = resolver.Resolve("INSTALLFOLDER");
        var second = resolver.Resolve("INSTALLFOLDER");

        Assert.Equal(first, second);
    }

    [Fact]
    public void IsStandardDirectory_KnownFolders()
    {
        Assert.True(DirectoryResolver.IsStandardDirectory("ProgramFilesFolder"));
        Assert.True(DirectoryResolver.IsStandardDirectory("TARGETDIR"));
        Assert.True(DirectoryResolver.IsStandardDirectory("SystemFolder"));
        Assert.False(DirectoryResolver.IsStandardDirectory("INSTALLFOLDER"));
        Assert.False(DirectoryResolver.IsStandardDirectory("CustomDir"));
    }

    [Fact]
    public void GetKnownFolder_ReturnsCorrectFolder()
    {
        Assert.Same(KnownFolder.ProgramFiles, DirectoryResolver.GetKnownFolder("ProgramFilesFolder"));
        Assert.Same(KnownFolder.ProgramFiles64, DirectoryResolver.GetKnownFolder("ProgramFiles64Folder"));
        Assert.Same(KnownFolder.CommonAppData, DirectoryResolver.GetKnownFolder("CommonAppDataFolder"));
        Assert.Same(KnownFolder.DesktopFolder, DirectoryResolver.GetKnownFolder("DesktopFolder"));
        Assert.Null(DirectoryResolver.GetKnownFolder("INSTALLFOLDER"));
    }

    [Fact]
    public void FindRootFolder_StandardRoot_ReturnsKnownFolder()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "ProgramFilesFolder", DefaultDir = "." },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "INSTALLFOLDER", ParentDirectoryId = "ProgramFilesFolder", DefaultDir = "MyApp" }
        ]);

        var (root, relativePath) = resolver.FindRootFolder("INSTALLFOLDER");

        Assert.Same(KnownFolder.ProgramFiles, root);
        Assert.Equal("MyApp", relativePath);
    }

    [Fact]
    public void FindRootFolder_NoStandardRoot_ReturnsNull()
    {
        var resolver = new DirectoryResolver([
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "CustomRoot", DefaultDir = "Root" },
            new DirectoryTableReader.DirectoryEntry { DirectoryId = "SubDir", ParentDirectoryId = "CustomRoot", DefaultDir = "Sub" }
        ]);

        var (root, relativePath) = resolver.FindRootFolder("SubDir");

        Assert.Null(root);
    }

    [Fact]
    public void ParseDirectoryName_Simple()
    {
        Assert.Equal("MyApp", DirectoryResolver.ParseDirectoryName("MyApp"));
    }

    [Fact]
    public void ParseDirectoryName_ShortLong()
    {
        Assert.Equal("My Application", DirectoryResolver.ParseDirectoryName("MYAPP~1|My Application"));
    }

    [Fact]
    public void ParseDirectoryName_WithSourceDir()
    {
        Assert.Equal("MyApp", DirectoryResolver.ParseDirectoryName("MyApp:src"));
    }

    [Fact]
    public void ParseDirectoryName_Empty()
    {
        Assert.Equal(string.Empty, DirectoryResolver.ParseDirectoryName(""));
    }
}
