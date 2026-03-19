using System.IO;
using Microsoft.Win32;

namespace FalkForge.Plugins.FileSystem;

internal sealed class FolderBrowser : IFolderBrowser
{
    public string? BrowseForFolder(string? initialDirectory = null, string? description = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = description ?? "Select folder",
            Multiselect = false
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}