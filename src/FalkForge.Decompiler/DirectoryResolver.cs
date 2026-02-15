using FalkForge.Decompiler.TableReaders;

namespace FalkForge.Decompiler;

/// <summary>
/// Resolves MSI Directory table parent-child relationships into full paths.
/// Handles standard MSI directory tokens (ProgramFilesFolder, etc.) and custom directories.
/// </summary>
public sealed class DirectoryResolver
{
    private readonly Dictionary<string, DirectoryTableReader.DirectoryEntry> _entries;
    private readonly Dictionary<string, string> _resolvedPaths = new(StringComparer.Ordinal);

    // Standard MSI directory tokens that map to well-known system folders
    private static readonly Dictionary<string, string> StandardDirectories = new(StringComparer.Ordinal)
    {
        ["TARGETDIR"] = "SourceDir",
        ["ProgramFilesFolder"] = "ProgramFiles",
        ["ProgramFiles64Folder"] = "ProgramFiles64",
        ["CommonAppDataFolder"] = "ProgramData",
        ["LocalAppDataFolder"] = "LocalAppData",
        ["AppDataFolder"] = "AppData",
        ["SystemFolder"] = "System32",
        ["System64Folder"] = "System64",
        ["WindowsFolder"] = "Windows",
        ["TempFolder"] = "Temp",
        ["DesktopFolder"] = "Desktop",
        ["StartMenuFolder"] = "StartMenu",
        ["ProgramMenuFolder"] = "Programs",
        ["StartupFolder"] = "Startup",
        ["CommonFilesFolder"] = "CommonFiles",
        ["CommonFiles64Folder"] = "CommonFiles64",
        ["FontsFolder"] = "Fonts",
        ["FavoritesFolder"] = "Favorites",
        ["PersonalFolder"] = "Personal",
        ["SendToFolder"] = "SendTo",
        ["AdminToolsFolder"] = "AdminTools"
    };

    public DirectoryResolver(IReadOnlyList<DirectoryTableReader.DirectoryEntry> entries)
    {
        _entries = new Dictionary<string, DirectoryTableReader.DirectoryEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            _entries[entry.DirectoryId] = entry;
        }
    }

    /// <summary>
    /// Resolves a directory ID to its full path string.
    /// </summary>
    public string Resolve(string directoryId)
    {
        if (_resolvedPaths.TryGetValue(directoryId, out var cached))
            return cached;

        var path = ResolveInternal(directoryId, []);
        _resolvedPaths[directoryId] = path;
        return path;
    }

    /// <summary>
    /// Returns the <see cref="KnownFolder"/> token for a directory ID, or null if not a standard directory.
    /// </summary>
    public static KnownFolder? GetKnownFolder(string directoryId)
    {
        return directoryId switch
        {
            "ProgramFilesFolder" => KnownFolder.ProgramFiles,
            "ProgramFiles64Folder" => KnownFolder.ProgramFiles64,
            "CommonAppDataFolder" => KnownFolder.CommonAppData,
            "LocalAppDataFolder" => KnownFolder.LocalAppData,
            "AppDataFolder" => KnownFolder.AppData,
            "SystemFolder" => KnownFolder.SystemFolder,
            "System64Folder" => KnownFolder.System64Folder,
            "WindowsFolder" => KnownFolder.WindowsFolder,
            "TempFolder" => KnownFolder.TempFolder,
            "DesktopFolder" => KnownFolder.DesktopFolder,
            "StartMenuFolder" => KnownFolder.StartMenuFolder,
            "ProgramMenuFolder" => KnownFolder.ProgramMenuFolder,
            "StartupFolder" => KnownFolder.StartupFolder,
            "CommonFilesFolder" => KnownFolder.CommonFilesFolder,
            "CommonFiles64Folder" => KnownFolder.CommonFiles64Folder,
            "FontsFolder" => KnownFolder.FontsFolder,
            _ => null
        };
    }

    /// <summary>
    /// Determines if a directory ID is a standard MSI directory.
    /// </summary>
    public static bool IsStandardDirectory(string directoryId)
    {
        return StandardDirectories.ContainsKey(directoryId);
    }

    /// <summary>
    /// Finds the root known folder for a directory by walking up the parent chain.
    /// Returns the KnownFolder and the relative path from it.
    /// </summary>
    public (KnownFolder? Root, string RelativePath) FindRootFolder(string directoryId)
    {
        var segments = new List<string>();
        var currentId = directoryId;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (currentId is not null && visited.Add(currentId))
        {
            var knownFolder = GetKnownFolder(currentId);
            if (knownFolder is not null)
            {
                segments.Reverse();
                return (knownFolder, string.Join("/", segments));
            }

            if (_entries.TryGetValue(currentId, out var entry))
            {
                var dirName = ParseDirectoryName(entry.DefaultDir);
                if (!string.IsNullOrEmpty(dirName) && dirName != "." && dirName != "SourceDir")
                    segments.Add(dirName);

                currentId = entry.ParentDirectoryId;
            }
            else
            {
                break;
            }
        }

        segments.Reverse();
        return (null, string.Join("/", segments));
    }

    private string ResolveInternal(string directoryId, HashSet<string> visited)
    {
        if (!visited.Add(directoryId))
            return directoryId; // Circular reference protection

        // Standard directory?
        if (StandardDirectories.TryGetValue(directoryId, out var standardName))
            return standardName;

        // Custom directory
        if (!_entries.TryGetValue(directoryId, out var entry))
            return directoryId;

        var dirName = ParseDirectoryName(entry.DefaultDir);

        if (entry.ParentDirectoryId is null)
            return string.IsNullOrEmpty(dirName) || dirName == "." ? directoryId : dirName;

        var parentPath = ResolveInternal(entry.ParentDirectoryId, visited);
        return string.IsNullOrEmpty(dirName) || dirName == "."
            ? parentPath
            : $"{parentPath}/{dirName}";
    }

    /// <summary>
    /// Parses the DefaultDir column value. Format: "short:source|long:source" or "short|long" or just "name".
    /// The target directory name is before the colon (if present).
    /// </summary>
    internal static string ParseDirectoryName(string defaultDir)
    {
        if (string.IsNullOrEmpty(defaultDir))
            return string.Empty;

        // Split target:source — we want the target (left of colon)
        var colonIndex = defaultDir.IndexOf(':');
        var targetPart = colonIndex >= 0 ? defaultDir[..colonIndex] : defaultDir;

        // Split short|long — we want the long name (right of pipe)
        var pipeIndex = targetPart.IndexOf('|');
        return pipeIndex >= 0 ? targetPart[(pipeIndex + 1)..] : targetPart;
    }
}
