using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class ComponentResolverTests
{
    private static PackageModel BuildPackageWithSingleFile(MockFileSystem fs)
    {
        fs.AddFile("C:/build/app.exe", size: 4096);

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });
    }

    [Fact]
    public void Resolve_SingleFile_CreatesOneComponent()
    {
        var fs = new MockFileSystem();
        var package = BuildPackageWithSingleFile(fs);
        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Components);
        Assert.Single(result.Value.Files);
    }

    [Fact]
    public void Resolve_SingleFile_ComponentIdContainsFileName()
    {
        var fs = new MockFileSystem();
        var package = BuildPackageWithSingleFile(fs);
        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        var component = result.Value.Components[0];
        Assert.StartsWith("C_", component.Id);
        Assert.Contains("app.exe", component.Id);
    }

    [Fact]
    public void Resolve_SingleFile_ComponentGuidIsDeterministic()
    {
        var fs = new MockFileSystem();
        var package = BuildPackageWithSingleFile(fs);
        var resolver = new ComponentResolver(fs);

        var result1 = resolver.Resolve(package);
        var result2 = resolver.Resolve(package);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal(result1.Value.Components[0].Guid, result2.Value.Components[0].Guid);
        Assert.NotEqual(Guid.Empty, result1.Value.Components[0].Guid);
    }

    [Fact]
    public void Resolve_SingleFile_FileIdContainsFileName()
    {
        var fs = new MockFileSystem();
        var package = BuildPackageWithSingleFile(fs);
        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        var file = result.Value.Files[0];
        Assert.StartsWith("F_", file.FileId);
        Assert.Contains("app.exe", file.FileId);
    }

    [Fact]
    public void Resolve_DirectoryHarvest_ExpandsToMultipleComponents()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/app.exe", size: 2048);
        fs.AddFile("C:/output/config.json", size: 512);
        fs.AddFile("C:/output/lib/helper.dll", size: 1024);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/output").To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Components.Count);
        Assert.Equal(3, result.Value.Files.Count);
    }

    [Fact]
    public void Resolve_EmptyPackage_ProducesEmptyResolution()
    {
        var fs = new MockFileSystem();
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Components);
        Assert.Empty(result.Value.Files);
    }

    [Fact]
    public void Resolve_FileSizes_AreReadFromFileSystem()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/big.exe", size: 10_000_000);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/big.exe").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Equal(10_000_000, result.Value.Files[0].FileSize);
    }

    [Fact]
    public void Resolve_NonExistentFile_FileSize_IsZero()
    {
        var fs = new MockFileSystem();
        // Don't add the file to the mock FS

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/missing/ghost.exe").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Files[0].FileSize);
    }

    [Fact]
    public void Resolve_DirectoryHarvest_SubdirectoryFiles_HaveExtendedTargetPath()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/sub/nested.dll", size: 256);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/output").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        var file = result.Value.Files[0];
        Assert.Equal("nested.dll", file.FileName);
        // Target directory should include the subdirectory
        Assert.Contains("sub", file.TargetDirectory.RelativePath);
    }

    [Fact]
    public void Resolve_MultipleFiles_EachHasUniqueComponentId()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/app.exe", size: 1000);
        fs.AddFile("C:/build/lib.dll", size: 2000);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("C:/build/app.exe")
                .Add("C:/build/lib.dll")
                .To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Components.Count);
        var ids = result.Value.Components.Select(c => c.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public void Resolve_Component_HasCorrectDirectory()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/app.exe", size: 100);

        var targetDir = KnownFolder.ProgramFiles / "Corp" / "App";
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/app.exe").To(targetDir));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetDir, result.Value.Components[0].Directory);
    }

    [Fact]
    public void Resolve_Component_KeyPathPointsToFileId()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/app.exe", size: 100);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/app.exe").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        var component = result.Value.Components[0];
        var file = result.Value.Files[0];
        Assert.Equal(file.FileId, component.KeyPath);
    }

    [Fact]
    public void Resolve_ResolvedPackage_ReferencesOriginalPackage()
    {
        var fs = new MockFileSystem();
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Same(package, result.Value.Package);
    }

    [Fact]
    public void Resolve_WildcardFile_NonExistentDirectory_IsSkipped()
    {
        var fs = new MockFileSystem();
        // Directory doesn't exist in mock FS

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/nonexistent").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);

        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Components);
    }

    [Fact]
    public void Resolve_DeterministicGuids_SameFileInSameDir_ProduceSameGuid()
    {
        var fs1 = new MockFileSystem();
        fs1.AddFile("C:/build/app.exe", size: 100);
        var fs2 = new MockFileSystem();
        fs2.AddFile("C:/build/app.exe", size: 200); // Different size

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/app.exe").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver1 = new ComponentResolver(fs1);
        var resolver2 = new ComponentResolver(fs2);

        var result1 = resolver1.Resolve(package);
        var result2 = resolver2.Resolve(package);

        // GUIDs are deterministic based on path, not content
        Assert.Equal(result1.Value.Components[0].Guid, result2.Value.Components[0].Guid);
    }
}
