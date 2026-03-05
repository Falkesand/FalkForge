using Xunit;

namespace FalkForge.Core.Tests;

public sealed class KnownFolderTests
{
    [Theory]
    [InlineData(nameof(KnownFolder.ProgramFiles), "ProgramFilesFolder", "Program Files")]
    [InlineData(nameof(KnownFolder.ProgramFiles64), "ProgramFiles64Folder", "Program Files (64-bit)")]
    [InlineData(nameof(KnownFolder.CommonAppData), "CommonAppDataFolder", "ProgramData")]
    public void KnownFolder_HasCorrectTokenAndDisplayName(string propertyName, string expectedToken, string expectedDisplayName)
    {
        var prop = typeof(KnownFolder).GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var folder = (KnownFolder)prop.GetValue(null)!;
        Assert.Equal(expectedToken, folder.Token);
        Assert.Equal(expectedDisplayName, folder.DisplayName);
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
