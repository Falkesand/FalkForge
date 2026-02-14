using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class KnownFolderTests
{
    [Fact]
    public void ProgramFiles_HasCorrectToken()
    {
        Assert.Equal("ProgramFilesFolder", KnownFolder.ProgramFiles.Token);
    }

    [Fact]
    public void ProgramFiles64_HasCorrectToken()
    {
        Assert.Equal("ProgramFiles64Folder", KnownFolder.ProgramFiles64.Token);
    }

    [Fact]
    public void CommonAppData_HasCorrectToken()
    {
        Assert.Equal("CommonAppDataFolder", KnownFolder.CommonAppData.Token);
    }

    [Fact]
    public void ProgramFiles_HasCorrectDisplayName()
    {
        Assert.Equal("Program Files", KnownFolder.ProgramFiles.DisplayName);
    }

    [Fact]
    public void SlashOperator_CreatesInstallPath()
    {
        var path = KnownFolder.ProgramFiles / "MyApp";

        Assert.NotNull(path);
        Assert.Equal(KnownFolder.ProgramFiles, path.Root);
        Assert.Equal("MyApp", path.RelativePath);
    }

    [Fact]
    public void ChainingSlashOperator_CreatesNestedPath()
    {
        var path = KnownFolder.ProgramFiles / "Contoso" / "MyApp";

        Assert.Equal("Contoso/MyApp", path.RelativePath);
        Assert.Equal(KnownFolder.ProgramFiles, path.Root);
    }

    [Fact]
    public void Indexer_CreatesInstallPath()
    {
        var path = KnownFolder.ProgramFiles["MyApp"];

        Assert.NotNull(path);
        Assert.Equal(KnownFolder.ProgramFiles, path.Root);
        Assert.Equal("MyApp", path.RelativePath);
    }

    [Fact]
    public void Segments_AreParsedCorrectly()
    {
        var path = KnownFolder.ProgramFiles / "Contoso" / "MyApp";

        Assert.Equal(2, path.Segments.Count);
        Assert.Equal("Contoso", path.Segments[0]);
        Assert.Equal("MyApp", path.Segments[1]);
    }

    [Fact]
    public void ToString_IncludesTokenInBrackets()
    {
        var folder = KnownFolder.ProgramFiles;

        Assert.Equal("[ProgramFilesFolder]", folder.ToString());
    }

    [Fact]
    public void AllKnownFolders_HaveNonEmptyTokens()
    {
        var folders = new[]
        {
            KnownFolder.ProgramFiles, KnownFolder.ProgramFiles64,
            KnownFolder.CommonAppData, KnownFolder.LocalAppData,
            KnownFolder.AppData, KnownFolder.SystemFolder,
            KnownFolder.System64Folder, KnownFolder.WindowsFolder,
            KnownFolder.TempFolder, KnownFolder.DesktopFolder,
            KnownFolder.StartMenuFolder, KnownFolder.ProgramMenuFolder,
            KnownFolder.StartupFolder, KnownFolder.CommonFilesFolder,
            KnownFolder.CommonFiles64Folder, KnownFolder.FontsFolder
        };

        foreach (var folder in folders)
        {
            Assert.False(string.IsNullOrEmpty(folder.Token));
            Assert.False(string.IsNullOrEmpty(folder.DisplayName));
        }
    }
}
