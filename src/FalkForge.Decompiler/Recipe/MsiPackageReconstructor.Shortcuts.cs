using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Shortcut-table reconstruction: maps raw <see cref="ShortcutRow"/> entries into
/// <see cref="ShortcutModel"/>, decoding the short|long name convention and
/// mapping the target directory id to a well-known <see cref="ShortcutLocation"/>.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private static List<ShortcutModel> BuildShortcuts(IReadOnlyList<ShortcutRow> shortcutRows)
    {
        return shortcutRows
            .Select(r =>
            {
                var longName = ParseLongFileName(r.Name);
                var location = MapShortcutLocation(r.Directory_);
                return new ShortcutModel
                {
                    Name = longName,
                    TargetFile = r.Target,
                    Locations = location is not null ? [location.Value] : [],
                    WorkingDirectory = r.WkDir,
                    Arguments = r.Arguments,
                    Description = r.Description,
                    IconFile = r.Icon_,
                    IconIndex = r.IconIndex ?? 0
                };
            })
            .ToList();
    }

    private static ShortcutLocation? MapShortcutLocation(string directoryId) => directoryId switch
    {
        "DesktopFolder"    => ShortcutLocation.Desktop,
        "StartMenuFolder"  => ShortcutLocation.StartMenu,
        "ProgramMenuFolder" => ShortcutLocation.StartMenu,
        "StartupFolder"    => ShortcutLocation.Startup,
        _ when directoryId.Contains("Desktop",     StringComparison.OrdinalIgnoreCase) => ShortcutLocation.Desktop,
        _ when directoryId.Contains("StartMenu",   StringComparison.OrdinalIgnoreCase) => ShortcutLocation.StartMenu,
        _ when directoryId.Contains("ProgramMenu", StringComparison.OrdinalIgnoreCase) => ShortcutLocation.StartMenu,
        _ when directoryId.Contains("Startup",     StringComparison.OrdinalIgnoreCase) => ShortcutLocation.Startup,
        _ => null
    };
}
