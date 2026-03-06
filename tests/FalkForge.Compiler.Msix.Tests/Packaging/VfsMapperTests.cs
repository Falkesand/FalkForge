using FalkForge.Compiler.Msix.Packaging;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests.Packaging;

public sealed class VfsMapperTests
{
    private static MsixModel CreateModel(
        IReadOnlyList<FileEntryModel>? files = null,
        ProcessorArchitecture architecture = ProcessorArchitecture.X64,
        VfsMappingMode mappingMode = VfsMappingMode.Auto,
        IReadOnlyList<VfsOverride>? overrides = null)
    {
        return new MsixModel
        {
            Name = "Test.App",
            Publisher = "CN=Test",
            Version = new Version(1, 0, 0, 0),
            DisplayName = "Test App",
            PublisherDisplayName = "Test",
            Applications = [new MsixApplication
            {
                Id = "App",
                Executable = "test.exe",
                VisualElements = new MsixVisualElements { DisplayName = "Test" },
            }],
            Files = files ?? [],
            Architecture = architecture,
            VfsMapping = mappingMode,
            VfsOverrides = overrides ?? [],
        };
    }

    private static FileEntryModel CreateFile(
        KnownFolder root,
        string relativePath = "",
        string fileName = "test.dll",
        string sourcePath = "C:/source/test.dll")
    {
        return new FileEntryModel
        {
            SourcePath = sourcePath,
            TargetDirectory = root / relativePath,
            FileName = fileName,
        };
    }

    [Fact]
    public void Resolve_AutoMode_ProgramFilesFolder_MapsToVfs()
    {
        var file = CreateFile(KnownFolder.ProgramFiles, "MyApp", "app.exe");
        var model = CreateModel(files: [file]);

        var result = VfsMapper.Resolve(model);

        Assert.Single(result);
        Assert.Equal("VFS/ProgramFilesX64/MyApp/app.exe", result[0].PackageRelativePath);
    }

    [Fact]
    public void Resolve_AutoMode_SystemFolder_MapsToVfs()
    {
        var file = CreateFile(KnownFolder.SystemFolder, fileName: "mylib.dll");
        var model = CreateModel(files: [file]);

        var result = VfsMapper.Resolve(model);

        Assert.Single(result);
        Assert.Equal("VFS/SystemX64/mylib.dll", result[0].PackageRelativePath);
    }

    [Fact]
    public void Resolve_AutoMode_CommonAppData_MapsToVfs()
    {
        var file = CreateFile(KnownFolder.CommonAppData, "MyApp", "config.json");
        var model = CreateModel(files: [file]);

        var result = VfsMapper.Resolve(model);

        Assert.Single(result);
        Assert.Equal("VFS/CommonAppData/MyApp/config.json", result[0].PackageRelativePath);
    }

    [Fact]
    public void Resolve_AutoMode_UnknownFolder_MapsToRoot()
    {
        var file = CreateFile(KnownFolder.TempFolder, "staging", "data.bin");
        var model = CreateModel(files: [file]);

        var result = VfsMapper.Resolve(model);

        Assert.Single(result);
        Assert.Equal("data.bin", result[0].PackageRelativePath);
    }

    [Fact]
    public void Resolve_AutoMode_X86Package_UsesProgramFilesX86()
    {
        var file = CreateFile(KnownFolder.ProgramFiles, "MyApp", "app.exe");
        var model = CreateModel(files: [file], architecture: ProcessorArchitecture.X86);

        var result = VfsMapper.Resolve(model);

        Assert.Single(result);
        Assert.Equal("VFS/ProgramFilesX86/MyApp/app.exe", result[0].PackageRelativePath);
    }

    [Fact]
    public void Resolve_ManualMode_UsesOverrides()
    {
        var file = CreateFile(
            KnownFolder.ProgramFiles,
            "MyApp",
            "app.exe",
            sourcePath: "C:/build/output/app.exe");

        var overrides = new[]
        {
            new VfsOverride
            {
                SourceDirectory = "C:/build/output",
                PackageRelativePath = "VFS/ProgramFilesX64/Custom",
            },
        };

        var model = CreateModel(
            files: [file],
            mappingMode: VfsMappingMode.Manual,
            overrides: overrides);

        var result = VfsMapper.Resolve(model);

        Assert.Single(result);
        Assert.Equal("VFS/ProgramFilesX64/Custom/app.exe", result[0].PackageRelativePath);
    }

    [Fact]
    public void Resolve_ManualMode_NoOverride_MapsToRoot()
    {
        var file = CreateFile(
            KnownFolder.ProgramFiles,
            "MyApp",
            "app.exe",
            sourcePath: "C:/other/app.exe");

        var model = CreateModel(
            files: [file],
            mappingMode: VfsMappingMode.Manual);

        var result = VfsMapper.Resolve(model);

        Assert.Single(result);
        Assert.Equal("app.exe", result[0].PackageRelativePath);
    }

    [Fact]
    public void Resolve_EmptyFiles_ReturnsEmpty()
    {
        var model = CreateModel();

        var result = VfsMapper.Resolve(model);

        Assert.Empty(result);
    }
}
