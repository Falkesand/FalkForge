using FalkForge.Models;

namespace FalkForge.Compiler.Msix.Packaging;

public static class VfsMapper
{
    private static readonly Dictionary<string, string> KnownFolderToVfs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProgramFilesFolder"] = "VFS/ProgramFilesX64",
        ["ProgramFiles64Folder"] = "VFS/ProgramFilesX64",
        ["ProgramFilesX86Folder"] = "VFS/ProgramFilesX86",
        ["CommonFilesFolder"] = "VFS/ProgramFilesCommonX64",
        ["CommonFiles64Folder"] = "VFS/ProgramFilesCommonX64",
        ["SystemFolder"] = "VFS/SystemX64",
        ["System64Folder"] = "VFS/SystemX64",
        ["WindowsFolder"] = "VFS/Windows",
        ["CommonAppDataFolder"] = "VFS/CommonAppData",
        ["AppDataFolder"] = "VFS/AppData",
        ["LocalAppDataFolder"] = "VFS/LocalAppData",
        ["FontsFolder"] = "VFS/Fonts",
    };

    private static readonly Dictionary<string, string> X86Remapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProgramFilesFolder"] = "VFS/ProgramFilesX86",
        ["CommonFilesFolder"] = "VFS/ProgramFilesCommonX86",
    };

    public static IReadOnlyList<VfsFileEntry> Resolve(MsixModel model)
    {
        return model.VfsMapping switch
        {
            VfsMappingMode.Auto => ResolveAuto(model),
            VfsMappingMode.Manual => ResolveManual(model),
            _ => ResolveAuto(model),
        };
    }

    private static List<VfsFileEntry> ResolveAuto(MsixModel model)
    {
        var results = new List<VfsFileEntry>(model.Files.Count);

        foreach (var file in model.Files)
        {
            var overridePath = FindOverride(file, model.VfsOverrides);
            if (overridePath is not null)
            {
                results.Add(new VfsFileEntry
                {
                    SourcePath = file.SourcePath,
                    PackageRelativePath = CombinePath(overridePath, file.FileName),
                });
                continue;
            }

            var folderToken = file.TargetDirectory.Root.Token;
            var vfsRoot = ResolveVfsRoot(folderToken, model.Architecture);

            if (vfsRoot is not null)
            {
                var relativePath = file.TargetDirectory.RelativePath;
                var packagePath = string.IsNullOrEmpty(relativePath)
                    ? $"{vfsRoot}/{file.FileName}"
                    : $"{vfsRoot}/{relativePath}/{file.FileName}";

                results.Add(new VfsFileEntry
                {
                    SourcePath = file.SourcePath,
                    PackageRelativePath = packagePath,
                });
            }
            else
            {
                results.Add(new VfsFileEntry
                {
                    SourcePath = file.SourcePath,
                    PackageRelativePath = file.FileName,
                });
            }
        }

        return results;
    }

    private static List<VfsFileEntry> ResolveManual(MsixModel model)
    {
        var results = new List<VfsFileEntry>(model.Files.Count);

        foreach (var file in model.Files)
        {
            var overridePath = FindOverride(file, model.VfsOverrides);

            results.Add(new VfsFileEntry
            {
                SourcePath = file.SourcePath,
                PackageRelativePath = overridePath is not null
                    ? CombinePath(overridePath, file.FileName)
                    : file.FileName,
            });
        }

        return results;
    }

    private static string? ResolveVfsRoot(string folderToken, ProcessorArchitecture architecture)
    {
        if (architecture == ProcessorArchitecture.X86 &&
            X86Remapping.TryGetValue(folderToken, out var x86Path))
        {
            return x86Path;
        }

        return KnownFolderToVfs.TryGetValue(folderToken, out var vfsPath) ? vfsPath : null;
    }

    private static string? FindOverride(FileEntryModel file, IReadOnlyList<VfsOverride> overrides)
    {
        var sourceDir = Path.GetDirectoryName(file.SourcePath)?.Replace('\\', '/');
        if (sourceDir is null)
        {
            return null;
        }

        foreach (var vfsOverride in overrides)
        {
            var overrideDir = vfsOverride.SourceDirectory.Replace('\\', '/');
            if (string.Equals(sourceDir, overrideDir, StringComparison.OrdinalIgnoreCase))
            {
                return vfsOverride.PackageRelativePath;
            }
        }

        return null;
    }

    private static string CombinePath(string basePath, string fileName)
    {
        return string.IsNullOrEmpty(basePath) ? fileName : $"{basePath}/{fileName}";
    }
}
