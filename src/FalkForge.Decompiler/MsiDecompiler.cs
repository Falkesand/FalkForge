using System.Runtime.Versioning;
using FalkForge.Decompiler.TableReaders;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Decompiles an MSI database into a <see cref="PackageModel"/> or fluent C# source code.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiDecompiler
{
    private readonly IMsiTableAccess? _tableAccess;

    /// <summary>
    /// Creates a decompiler that will open the MSI file at the given path.
    /// </summary>
    public MsiDecompiler()
    {
    }

    /// <summary>
    /// Creates a decompiler with an injected table access for testing.
    /// </summary>
    public MsiDecompiler(IMsiTableAccess tableAccess)
    {
        _tableAccess = tableAccess;
    }

    /// <summary>
    /// Decompiles an MSI file into a <see cref="PackageModel"/>.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<PackageModel> Decompile(string msiPath)
    {
        if (_tableAccess is not null)
            return DecompileFromAccess(_tableAccess);

        if (string.IsNullOrWhiteSpace(msiPath))
            return Result<PackageModel>.Failure(ErrorKind.Validation, "DEC001: MSI path cannot be null or empty.");

        if (!File.Exists(msiPath))
            return Result<PackageModel>.Failure(ErrorKind.FileNotFound, $"DEC001: Cannot open MSI file '{msiPath}'. File not found.");

        var accessResult = MsiTableAccess.Open(msiPath);
        if (accessResult.IsFailure)
            return Result<PackageModel>.Failure(accessResult.Error);

        using var access = accessResult.Value;
        return DecompileFromAccess(access);
    }

    /// <summary>
    /// Decompiles an MSI file and emits fluent C# source code.
    /// When a table access was injected via constructor, <paramref name="msiPath"/> is ignored.
    /// </summary>
    public Result<string> DecompileToCSharp(string msiPath)
    {
        var modelResult = Decompile(msiPath);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(modelResult.Value);
        return source;
    }

    private static Result<PackageModel> DecompileFromAccess(IMsiTableAccess access)
    {
        // Read all properties first for metadata
        var propsResult = PropertyTableReader.ReadAll(access);
        if (propsResult.IsFailure)
            return Result<PackageModel>.Failure(propsResult.Error);

        var allProperties = propsResult.Value;

        // Read directory table and build resolver
        var dirResult = DirectoryTableReader.Read(access);
        if (dirResult.IsFailure)
            return Result<PackageModel>.Failure(dirResult.Error);

        var directoryResolver = new DirectoryResolver(dirResult.Value);

        // Read components
        var componentsResult = ComponentTableReader.Read(access);
        if (componentsResult.IsFailure)
            return Result<PackageModel>.Failure(componentsResult.Error);

        // Build component-to-directory mapping
        var componentDirectoryMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var comp in componentsResult.Value)
        {
            componentDirectoryMap[comp.ComponentName] = comp.DirectoryId;
        }

        // Read files
        var filesResult = FileTableReader.Read(access);
        if (filesResult.IsFailure)
            return Result<PackageModel>.Failure(filesResult.Error);

        // Build component-to-condition mapping
        var componentConditionMap = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var comp in componentsResult.Value)
        {
            componentConditionMap[comp.ComponentName] = comp.Condition;
        }

        // Map files to FileEntryModels
        var fileEntries = new List<FileEntryModel>();
        foreach (var file in filesResult.Value)
        {
            var directoryId = componentDirectoryMap.GetValueOrDefault(file.ComponentRef, "TARGETDIR");
            var (root, relativePath) = directoryResolver.FindRootFolder(directoryId);

            var installPath = root is not null
                ? root / relativePath
                : KnownFolder.ProgramFiles / relativePath;

            // Check if this file is the key path for its component
            var component = componentsResult.Value.Find(c => c.ComponentName == file.ComponentRef);
            var isKeyPath = component?.KeyPath == file.FileKey;

            componentConditionMap.TryGetValue(file.ComponentRef, out var condition);

            fileEntries.Add(new FileEntryModel
            {
                SourcePath = file.FileName, // Best we can reconstruct
                TargetDirectory = installPath,
                FileName = file.FileName,
                IsKeyPath = isKeyPath,
                ComponentId = file.ComponentRef,
                ComponentCondition = condition
            });
        }

        // Read features
        var featuresResult = FeatureTableReader.Read(access);
        if (featuresResult.IsFailure)
            return Result<PackageModel>.Failure(featuresResult.Error);

        // Read registry entries
        var registryResult = RegistryTableReader.Read(access);
        if (registryResult.IsFailure)
            return Result<PackageModel>.Failure(registryResult.Error);

        // Read services
        var servicesResult = ServiceTableReader.Read(access);
        if (servicesResult.IsFailure)
            return Result<PackageModel>.Failure(servicesResult.Error);

        // Read shortcuts
        var shortcutsResult = ShortcutTableReader.Read(access);
        if (shortcutsResult.IsFailure)
            return Result<PackageModel>.Failure(shortcutsResult.Error);

        // Read user-defined properties
        var userPropertiesResult = PropertyTableReader.Read(access);
        if (userPropertiesResult.IsFailure)
            return Result<PackageModel>.Failure(userPropertiesResult.Error);

        // Read upgrade information
        var upgradeReadResult = UpgradeTableReader.Read(access);
        if (upgradeReadResult.IsFailure)
            return Result<PackageModel>.Failure(upgradeReadResult.Error);

        // Extract metadata from properties
        var name = allProperties.TryGetValue("ProductName", out var pn) ? pn : "Unknown";
        var manufacturer = allProperties.TryGetValue("Manufacturer", out var mfr) ? mfr : "Unknown";
        var versionStr = allProperties.TryGetValue("ProductVersion", out var pv) ? pv : "1.0.0";
        _ = Version.TryParse(versionStr, out var version);
        version ??= new Version(1, 0, 0);

        allProperties.TryGetValue("UpgradeCode", out var upgradeCodeStr);
        Guid.TryParse(upgradeCodeStr, out var upgradeCode);
        allProperties.TryGetValue("ProductCode", out var productCodeStr);
        Guid.TryParse(productCodeStr, out var productCode);

        // Determine install scope from ALLUSERS property
        var scope = InstallScope.PerMachine;
        if (allProperties.TryGetValue("ALLUSERS", out var allUsers))
        {
            if (allUsers == "2" || string.IsNullOrEmpty(allUsers))
                scope = InstallScope.PerUser;
        }

        // Build install directory from INSTALLFOLDER or INSTALLDIR or first custom directory
        InstallPath? defaultInstallDir = null;
        foreach (var dirName in new[] { "INSTALLFOLDER", "INSTALLDIR", "APPDIR" })
        {
            if (dirResult.Value.Exists(d => d.DirectoryId == dirName))
            {
                var (root, relPath) = directoryResolver.FindRootFolder(dirName);
                if (root is not null)
                {
                    defaultInstallDir = root / relPath;
                    break;
                }
            }
        }

        return new PackageModel
        {
            Name = name,
            Manufacturer = manufacturer,
            Version = version,
            UpgradeCode = upgradeCode,
            ProductCode = productCode,
            Scope = scope,
            DefaultInstallDirectory = defaultInstallDir,
            Files = fileEntries,
            Features = featuresResult.Value,
            RegistryEntries = registryResult.Value,
            Services = servicesResult.Value,
            Shortcuts = shortcutsResult.Value,
            Properties = userPropertiesResult.Value,
            MajorUpgrade = upgradeReadResult.Value.MajorUpgrade
        };
    }
}
