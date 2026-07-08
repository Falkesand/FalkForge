using System.IO;
using FalkForge.Diagnostics;
using Microsoft.Win32;

namespace FalkForge.Plugins.FileSystem;

internal sealed class FolderBrowser : IFolderBrowser
{
    private const string Category = "FolderBrowser";

    private readonly Func<string?, string?, string?> _showDialog;
    private readonly IFalkLogger? _logger;

    public FolderBrowser(IFalkLogger? logger = null) : this(ShowNativeDialog, logger)
    {
    }

    internal FolderBrowser(Func<string?, string?, string?> showDialog, IFalkLogger? logger = null)
    {
        _showDialog = showDialog;
        _logger = logger;
    }

    public string? BrowseForFolder(string? initialDirectory = null, string? description = null)
    {
        _logger?.Info(Category, "Opening folder browse dialog");
        string? selected;
        try
        {
            selected = _showDialog(initialDirectory, description);
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.Error, Category, "Folder browse dialog failed", ex,
                new Dictionary<string, string> { ["code"] = nameof(ErrorKind.PluginError) });
            throw;
        }

        if (selected is null)
            _logger?.Debug(Category, "Folder browse canceled by user");
        else
            _logger?.Info(Category, $"Folder selected: '{selected}'");

        return selected;
    }

    private static string? ShowNativeDialog(string? initialDirectory, string? description)
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