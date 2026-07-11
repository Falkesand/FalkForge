using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Msi.Recipe.Producers;
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

                    var sanitizedFileName = ProducerHelpers.SanitizeDirectoryId(fileName);
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
                        FileId = GenerateFileId(sanitizedFileName, componentId),
                        Vital = file.Vital
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
                var sanitizedFileName = ProducerHelpers.SanitizeDirectoryId(file.FileName);
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
                    FileId = GenerateFileId(sanitizedFileName, componentId),
                    Vital = file.Vital
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

        InstallPath defaultDirectory = package.DefaultInstallDirectory
            ?? KnownFolder.ProgramFiles / package.Manufacturer / package.Name;

        var serviceFeatureComponents = new Dictionary<string, string>();
        foreach (var service in package.Services)
        {
            if (service.FeatureRef is null)
            {
                continue;
            }

            var componentId = GenerateServiceComponentId(service.Name);
            var componentGuid = GuidUtility.CreateDeterministicGuid(
                GuidUtility.FalkForgeNamespace,
                $"service-component::{service.Name}");

            components.Add(new ResolvedComponent
            {
                Id = componentId,
                Guid = componentGuid,
                Directory = defaultDirectory,
                KeyPath = string.Empty,
                Files = [],
                FeatureRef = service.FeatureRef,
                Condition = service.ComponentCondition
            });
            serviceFeatureComponents[service.Name] = componentId;
        }

        var registryFeatureComponents = new Dictionary<int, string>();
        for (var i = 0; i < package.RegistryEntries.Count; i++)
        {
            var entry = package.RegistryEntries[i];
            if (entry.FeatureRef is null || entry.ComponentId is not null)
            {
                // Feature-gating requires a FeatureRef; an explicit ComponentId is a stronger,
                // user-authored override and always wins (see RegistryTableProducer).
                continue;
            }

            var componentId = GenerateRegistryComponentId(i, entry);
            var componentGuid = GuidUtility.CreateDeterministicGuid(
                GuidUtility.FalkForgeNamespace,
                $"registry-component::{i}::{entry.Root}::{entry.Key}::{entry.ValueName}");

            components.Add(new ResolvedComponent
            {
                Id = componentId,
                Guid = componentGuid,
                Directory = defaultDirectory,
                KeyPath = string.Empty,
                Files = [],
                FeatureRef = entry.FeatureRef
            });
            registryFeatureComponents[i] = componentId;
        }

        return new ResolvedPackage
        {
            Package = package,
            Components = components,
            Files = fileEntries,
            ServiceFeatureComponents = serviceFeatureComponents,
            RegistryFeatureComponents = registryFeatureComponents
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

    /// <summary>
    /// Generates the deterministic component id for a feature-gated service that has no
    /// file-based component to attach to. Keyed by service name only — MSI service names are
    /// unique within a package. The disambiguating hash is placed BEFORE the sanitized name so
    /// that when the id exceeds the 72-char MSI identifier limit and gets truncated, the hash
    /// (which carries the uniqueness) survives — two long service names sharing a truncated
    /// prefix must never collapse onto the same component id.
    /// </summary>
    private static string GenerateServiceComponentId(string serviceName)
    {
        var sanitized = ProducerHelpers.SanitizeDirectoryId(serviceName);
        var raw = $"C_SVC_{StableHash(serviceName)}_{sanitized}";
        return raw.Length > 72 ? raw[..72] : raw;
    }

    /// <summary>
    /// Generates the deterministic component id for a feature-gated registry entry that has no
    /// explicit ComponentId. Keyed by list index plus root/key/value-name so two entries with
    /// identical keys under different roots (or index) never collide. Unlike
    /// <see cref="GenerateServiceComponentId"/>, the hash stays last here because this id is
    /// always short (a numeric index plus an 8-char hash) and can never reach the 72-char
    /// truncation limit, so the hash is never at risk of being cut off.
    /// </summary>
    private static string GenerateRegistryComponentId(int index, RegistryEntryModel entry)
    {
        var hashInput = $"{index}|{entry.Root}|{entry.Key}|{entry.ValueName}";
        var raw = $"C_REG_{index}_{StableHash(hashInput)}";
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
}