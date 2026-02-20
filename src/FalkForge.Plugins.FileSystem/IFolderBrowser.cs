namespace FalkForge.Plugins.FileSystem;

public interface IFolderBrowser
{
    string? BrowseForFolder(string? initialDirectory = null, string? description = null);
}
