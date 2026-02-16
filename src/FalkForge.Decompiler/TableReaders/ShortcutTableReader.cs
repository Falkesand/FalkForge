using FalkForge.Models;

namespace FalkForge.Decompiler.TableReaders;

/// <summary>
/// Reads the Shortcut table from an MSI database.
/// Columns: Shortcut, Directory_, Name, Component_, Target, Arguments, Description, Hotkey, Icon_, IconIndex, ShowCmd, WkDir
/// </summary>
public static class ShortcutTableReader
{
    private static readonly string[] Columns = ["Shortcut", "Directory_", "Name", "Component_", "Target", "Arguments", "Description", "Hotkey", "Icon_", "IconIndex", "ShowCmd", "WkDir"];

    public static Result<List<ShortcutModel>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Shortcut");
        if (existsResult.IsFailure)
            return Result<List<ShortcutModel>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<ShortcutModel>>.Success([]);

        var rowsResult = tableAccess.QueryTable("Shortcut", Columns);
        if (rowsResult.IsFailure)
            return Result<List<ShortcutModel>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Shortcut table. {rowsResult.Error.Message}");

        var shortcuts = new List<ShortcutModel>();
        foreach (var row in rowsResult.Value)
        {
            _ = int.TryParse(row[9], out var iconIndex);

            // Parse the Name column (short|long format)
            var name = FileTableReader.ParseLongFileName(row[2] ?? string.Empty);
            var directoryId = row[1] ?? string.Empty;
            var location = MapShortcutLocation(directoryId);

            shortcuts.Add(new ShortcutModel
            {
                Name = name,
                TargetFile = row[4] ?? string.Empty,
                Locations = location is not null ? [location.Value] : [],
                WorkingDirectory = row[11],
                Arguments = row[5],
                Description = row[6],
                IconFile = row[8],
                IconIndex = iconIndex
            });
        }

        return shortcuts;
    }

    internal static ShortcutLocation? MapShortcutLocation(string directoryId) => directoryId switch
    {
        "DesktopFolder" => ShortcutLocation.Desktop,
        "StartMenuFolder" => ShortcutLocation.StartMenu,
        "ProgramMenuFolder" => ShortcutLocation.StartMenu,
        "StartupFolder" => ShortcutLocation.Startup,
        _ when directoryId.Contains("Desktop", StringComparison.OrdinalIgnoreCase) => ShortcutLocation.Desktop,
        _ when directoryId.Contains("StartMenu", StringComparison.OrdinalIgnoreCase) => ShortcutLocation.StartMenu,
        _ when directoryId.Contains("ProgramMenu", StringComparison.OrdinalIgnoreCase) => ShortcutLocation.StartMenu,
        _ when directoryId.Contains("Startup", StringComparison.OrdinalIgnoreCase) => ShortcutLocation.Startup,
        _ => null
    };
}
