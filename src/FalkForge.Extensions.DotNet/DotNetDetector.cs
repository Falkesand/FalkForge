namespace FalkForge.Extensions.DotNet;

using FalkForge.Platform;

public sealed class DotNetDetector
{
    private readonly IRegistry _registry;
    private readonly IFileSystem _fileSystem;

    public DotNetDetector(IRegistry registry, IFileSystem fileSystem)
    {
        _registry = registry;
        _fileSystem = fileSystem;
    }

    public Result<List<DotNetDetectionResult>> Detect()
    {
        var results = new List<DotNetDetectionResult>();

        foreach (var platform in Enum.GetValues<DotNetPlatform>())
        {
            DetectSharedFrameworks(platform, results);
            DetectHostfxrFromFileSystem(platform, results);
        }

        return results;
    }

    private void DetectSharedFrameworks(DotNetPlatform platform, List<DotNetDetectionResult> results)
    {
        var archKey = PlatformToArchKey(platform);

        // Detect each runtime type via the sharedfx registry keys
        foreach (var runtimeType in Enum.GetValues<DotNetRuntimeType>())
        {
            var runtimeName = RuntimeTypeToRegistryName(runtimeType);
            if (runtimeName is null)
                continue;

            var subKey = $@"SOFTWARE\dotnet\Setup\InstalledVersions\{archKey}\sharedfx\{runtimeName}";
            var versionNames = _registry.GetSubKeyNames("HKLM", subKey);

            foreach (var versionName in versionNames)
            {
                if (!Version.TryParse(versionName, out var version))
                    continue;

                // Check that the version key exists (value doesn't matter, presence means installed)
                var versionSubKey = $@"{subKey}\{versionName}";
                if (!_registry.KeyExists("HKLM", versionSubKey))
                    continue;

                var installPath = GetInstallPath(archKey, runtimeName, versionName);

                // Avoid duplicates (registry + filesystem may find the same version)
                if (!results.Exists(r =>
                    r.RuntimeType == runtimeType &&
                    r.Platform == platform &&
                    r.Version == version))
                {
                    results.Add(new DotNetDetectionResult(runtimeType, platform, version, installPath));
                }
            }
        }
    }

    private void DetectHostfxrFromFileSystem(DotNetPlatform platform, List<DotNetDetectionResult> results)
    {
        var programFilesBase = platform switch
        {
            DotNetPlatform.X86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            DotNetPlatform.X64 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            DotNetPlatform.Arm64 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            _ => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        var fxrPath = $@"{programFilesBase}\dotnet\host\fxr";

        if (!_fileSystem.DirectoryExists(fxrPath))
            return;

        var versionDirs = _fileSystem.GetDirectories(fxrPath);
        foreach (var versionDir in versionDirs)
        {
            var dirName = _fileSystem.GetFileName(versionDir);
            if (!Version.TryParse(dirName, out var version))
                continue;

            var hostfxrDll = $@"{versionDir}\hostfxr.dll";
            if (!_fileSystem.FileExists(hostfxrDll))
                continue;

            // hostfxr presence indicates the Runtime is installed
            if (!results.Exists(r =>
                r.RuntimeType == DotNetRuntimeType.Runtime &&
                r.Platform == platform &&
                r.Version == version))
            {
                results.Add(new DotNetDetectionResult(
                    DotNetRuntimeType.Runtime,
                    platform,
                    version,
                    versionDir));
            }
        }
    }

    private string? GetInstallPath(string archKey, string runtimeName, string versionName)
    {
        // Try to read the install location from registry
        var subKey = $@"SOFTWARE\dotnet\Setup\InstalledVersions\{archKey}\sharedfx\{runtimeName}\{versionName}";
        return _registry.GetStringValue("HKLM", subKey, "InstallPath");
    }

    internal static string PlatformToArchKey(DotNetPlatform platform) => platform switch
    {
        DotNetPlatform.X64 => "x64",
        DotNetPlatform.X86 => "x86",
        DotNetPlatform.Arm64 => "arm64",
        _ => "x64"
    };

    internal static string? RuntimeTypeToRegistryName(DotNetRuntimeType runtimeType) => runtimeType switch
    {
        DotNetRuntimeType.Runtime => "Microsoft.NETCore.App",
        DotNetRuntimeType.AspNetCore => "Microsoft.AspNetCore.App",
        DotNetRuntimeType.WindowsDesktop => "Microsoft.WindowsDesktop.App",
        DotNetRuntimeType.Sdk => null, // SDK is detected differently
        _ => null
    };
}
