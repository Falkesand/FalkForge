using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// File-table reconstruction: maps raw <see cref="FileRow"/> entries into
/// <see cref="FileEntryModel"/>, resolving each file's install directory through
/// the owning component and decoding the MSI short|long filename convention.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private static List<FileEntryModel> BuildFileEntries(
        IReadOnlyList<FileRow> fileRows,
        Dictionary<string, string> componentDirMap,
        Dictionary<string, string?> componentKeyMap,
        Dictionary<string, string?> componentCondMap,
        DirectoryResolver dirResolver)
    {
        var fileEntries = new List<FileEntryModel>(fileRows.Count);
        foreach (var f in fileRows)
        {
            var dirId = componentDirMap.GetValueOrDefault(f.Component_, "TARGETDIR");
            var (root, relativePath) = dirResolver.FindRootFolder(dirId);
            var installPath = root is not null
                ? root / relativePath
                : KnownFolder.ProgramFiles / relativePath;

            componentKeyMap.TryGetValue(f.Component_, out var keyPath);
            var isKeyPath = keyPath == f.File;

            componentCondMap.TryGetValue(f.Component_, out var condition);

            // FileName column uses short|long format — extract long name
            var longName = ParseLongFileName(f.FileName);

            fileEntries.Add(new FileEntryModel
            {
                SourcePath = longName,
                TargetDirectory = installPath,
                FileName = longName,
                IsKeyPath = isKeyPath,
                // msidbFileAttributesVital (512) SET means the file is vital; CLEAR means non-vital.
                // FileTableProducer emits the bit only for vital files, so the inverse is a bit test.
                Vital = (f.Attributes & FileAttributesVital) != 0,
                ComponentId = f.Component_,
                ComponentCondition = condition
            });
        }

        return fileEntries;
    }

    private static string ParseLongFileName(string msiFileName)
    {
        if (string.IsNullOrEmpty(msiFileName)) return string.Empty;
        var idx = msiFileName.IndexOf('|');
        return idx >= 0 ? msiFileName[(idx + 1)..] : msiFileName;
    }
}
