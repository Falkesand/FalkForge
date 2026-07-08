using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Models;
using FalkForge.Platform;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed class ComponentResolver
{
    private static readonly ConcurrentDictionary<string, string> _stableHashCache = new();

    private readonly IFileSystem _fileSystem;

    public ComponentResolver(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Result<ResolvedPackage> Resolve(PackageModel package)
    {
        var components = new List<ResolvedComponent>();
        var fileEntries = new List<ResolvedFile>();

        foreach (var file in package.Files)
            if (file.FileName == "*" && _fileSystem.DirectoryExists(file.SourcePath))
            {
                // Directory harvest
                var files = _fileSystem.GetFiles(file.SourcePath, "*", true);
                foreach (var filePath in files)
                {
                    var relativePath = _fileSystem.GetRelativePath(file.SourcePath, filePath);
                    var fileName = _fileSystem.GetFileName(filePath);
                    var subDir = _fileSystem.GetDirectoryName(relativePath);

                    var targetDir = string.IsNullOrEmpty(subDir)
                        ? file.TargetDirectory
                        : file.TargetDirectory / subDir.Replace('\\', '/');

                    var sanitizedFileName = SanitizeId(fileName);
                    var componentId = GenerateComponentId(targetDir, sanitizedFileName);
                    var componentGuid = GuidUtility.CreateDeterministicGuid(
                        GuidUtility.FalkForgeNamespace,
                        $"component::{targetDir}::{fileName}");

                    var resolvedFile = new ResolvedFile
                    {
                        SourcePath = _fileSystem.GetFullPath(filePath),
                        TargetDirectory = targetDir,
                        FileName = fileName,
                        FileSize = _fileSystem.GetFileSize(filePath),
                        ComponentId = componentId,
                        FileId = GenerateFileId(sanitizedFileName, componentId)
                    };

                    fileEntries.Add(resolvedFile);

                    components.Add(new ResolvedComponent
                    {
                        Id = componentId,
                        Guid = componentGuid,
                        Directory = targetDir,
                        KeyPath = resolvedFile.FileId,
                        Files = [resolvedFile],
                        FeatureRef = file.FeatureRef,
                        Condition = file.ComponentCondition,
                        NeverOverwrite = file.NeverOverwrite,
                        Permanent = file.Permanent
                    });
                }
            }
            else if (file.FileName != "*")
            {
                var fullPath = _fileSystem.GetFullPath(file.SourcePath);
                var sanitizedFileName = SanitizeId(file.FileName);
                var componentId = GenerateComponentId(file.TargetDirectory, sanitizedFileName);
                var componentGuid = file.ComponentGuid ?? GuidUtility.CreateDeterministicGuid(
                    GuidUtility.FalkForgeNamespace,
                    $"component::{file.TargetDirectory}::{file.FileName}");

                var resolvedFile = new ResolvedFile
                {
                    SourcePath = fullPath,
                    TargetDirectory = file.TargetDirectory,
                    FileName = file.FileName,
                    FileSize = _fileSystem.FileExists(fullPath) ? _fileSystem.GetFileSize(fullPath) : 0,
                    ComponentId = componentId,
                    FileId = GenerateFileId(sanitizedFileName, componentId)
                };

                fileEntries.Add(resolvedFile);

                components.Add(new ResolvedComponent
                {
                    Id = componentId,
                    Guid = componentGuid,
                    Directory = file.TargetDirectory,
                    KeyPath = resolvedFile.FileId,
                    Files = [resolvedFile],
                    FeatureRef = file.FeatureRef,
                    Condition = file.ComponentCondition,
                    NeverOverwrite = file.NeverOverwrite,
                    Permanent = file.Permanent
                });
            }

        return new ResolvedPackage
        {
            Package = package,
            Components = components,
            Files = fileEntries
        };
    }

    private static string GenerateComponentId(InstallPath directory, string sanitizedFileName)
    {
        var raw = $"C_{sanitizedFileName}_{StableHash(directory.ToString())}";
        return raw.Length > 72 ? raw[..72] : raw;
    }

    private static string GenerateFileId(string sanitizedFileName, string componentId)
    {
        var raw = $"F_{sanitizedFileName}_{StableHash(componentId)}";
        return raw.Length > 72 ? raw[..72] : raw;
    }

    private static string StableHash(string input)
    {
        return _stableHashCache.GetOrAdd(input, static key =>
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(bytes, 0, 4); // 8 hex chars, deterministic across runtimes
        });
    }

    private static string SanitizeId(string name)
    {
        // Avoid allocation for the common case where no replacement is needed.
        var needsReplacement = false;
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
            {
                needsReplacement = true;
                break;
            }
        }

        if (!needsReplacement)
        {
            return name;
        }

        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            sanitized[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
        }

        return new string(sanitized);
    }
}