using FalkForge.Extensions.DotNet;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Extensions.DotNet.Tests;

public sealed class DotNetDetectorTests
{
    [Fact]
    public void Detect_WithRuntimeInRegistry_ReturnsDetectedVersion()
    {
        var registry = new MockRegistry()
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App")
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.11");
        var fileSystem = new MockFileSystem();
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        var match = Assert.Single(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.Runtime &&
            r.Platform == DotNetPlatform.X64 &&
            r.Version == new Version(8, 0, 11));
        Assert.NotNull(match);
    }

    [Fact]
    public void Detect_WhenNothingInstalled_ReturnsEmptyList()
    {
        var registry = new MockRegistry();
        var fileSystem = new MockFileSystem();
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Detect_MultipleVersions_ReturnsAllVersions()
    {
        var registry = new MockRegistry()
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App")
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\6.0.35")
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.11");
        var fileSystem = new MockFileSystem();
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count(r =>
            r.RuntimeType == DotNetRuntimeType.Runtime &&
            r.Platform == DotNetPlatform.X64));
    }

    [Fact]
    public void Detect_AspNetCoreRuntime_ReturnsCorrectRuntimeType()
    {
        var registry = new MockRegistry()
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.AspNetCore.App")
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.AspNetCore.App\8.0.11");
        var fileSystem = new MockFileSystem();
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.AspNetCore &&
            r.Platform == DotNetPlatform.X64 &&
            r.Version == new Version(8, 0, 11));
    }

    [Fact]
    public void Detect_WindowsDesktopRuntime_ReturnsCorrectRuntimeType()
    {
        var registry = new MockRegistry()
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App")
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\8.0.11");
        var fileSystem = new MockFileSystem();
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.WindowsDesktop &&
            r.Platform == DotNetPlatform.X64 &&
            r.Version == new Version(8, 0, 11));
    }

    [Fact]
    public void Detect_X86Platform_ReturnsCorrectPlatform()
    {
        var registry = new MockRegistry()
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.NETCore.App")
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.NETCore.App\8.0.11");
        var fileSystem = new MockFileSystem();
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.Runtime &&
            r.Platform == DotNetPlatform.X86 &&
            r.Version == new Version(8, 0, 11));
    }

    [Fact]
    public void Detect_HostfxrOnDisk_DetectsRuntimeFromFileSystem()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var registry = new MockRegistry();
        var fileSystem = new MockFileSystem()
            .AddDirectory($@"{programFiles}\dotnet\host\fxr")
            .AddDirectory($@"{programFiles}\dotnet\host\fxr\8.0.11")
            .AddFile($@"{programFiles}\dotnet\host\fxr\8.0.11\hostfxr.dll");
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.Runtime &&
            r.Platform == DotNetPlatform.X64 &&
            r.Version == new Version(8, 0, 11));
    }

    [Fact]
    public void Detect_RegistryAndFileSystemSameVersion_NoDuplicates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var registry = new MockRegistry()
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App")
            .AddKey("HKLM", @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.11");
        var fileSystem = new MockFileSystem()
            .AddDirectory($@"{programFiles}\dotnet\host\fxr")
            .AddDirectory($@"{programFiles}\dotnet\host\fxr\8.0.11")
            .AddFile($@"{programFiles}\dotnet\host\fxr\8.0.11\hostfxr.dll");
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.Runtime &&
            r.Platform == DotNetPlatform.X64 &&
            r.Version == new Version(8, 0, 11));
    }

    [Fact]
    public void Detect_UsesEnvironmentProgramFilesPath_NotHardcoded()
    {
        // Verify detection works with the actual Environment.GetFolderPath values
        // rather than hardcoded "C:\Program Files" paths
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var registry = new MockRegistry();
        var fileSystem = new MockFileSystem()
            .AddDirectory($@"{programFilesX86}\dotnet\host\fxr")
            .AddDirectory($@"{programFilesX86}\dotnet\host\fxr\8.0.11")
            .AddFile($@"{programFilesX86}\dotnet\host\fxr\8.0.11\hostfxr.dll")
            .AddDirectory($@"{programFiles}\dotnet\host\fxr")
            .AddDirectory($@"{programFiles}\dotnet\host\fxr\9.0.0")
            .AddFile($@"{programFiles}\dotnet\host\fxr\9.0.0\hostfxr.dll");
        var detector = new DotNetDetector(registry, fileSystem);

        var result = detector.Detect();

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.Runtime &&
            r.Platform == DotNetPlatform.X86 &&
            r.Version == new Version(8, 0, 11));
        Assert.Contains(result.Value, r =>
            r.RuntimeType == DotNetRuntimeType.Runtime &&
            r.Platform == DotNetPlatform.X64 &&
            r.Version == new Version(9, 0, 0));
    }
}
