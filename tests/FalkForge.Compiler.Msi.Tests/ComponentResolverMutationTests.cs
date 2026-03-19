using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class ComponentResolverMutationTests
{
    private static ResolvedPackage ResolvePackageWithFile(
        string filePath, string fileName, InstallPath targetDir)
    {
        var fs = new MockFileSystem();
        fs.AddFile(filePath, size: 512);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add(filePath).To(targetDir));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void Resolve_ComponentId_IsTruncatedAt72Characters()
    {
        var fs = new MockFileSystem();
        // Use a very long filename to force truncation
        var longName = new string('a', 100) + ".dll";
        fs.AddFile($"C:/build/{longName}", size: 512);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add($"C:/build/{longName}").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Components[0].Id.Length <= 72,
            $"Component ID length {result.Value.Components[0].Id.Length} exceeds 72");
    }

    [Fact]
    public void Resolve_FileId_IsTruncatedAt72Characters()
    {
        var fs = new MockFileSystem();
        var longName = new string('b', 100) + ".exe";
        fs.AddFile($"C:/build/{longName}", size: 512);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add($"C:/build/{longName}").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Files[0].FileId.Length <= 72,
            $"File ID length {result.Value.Files[0].FileId.Length} exceeds 72");
    }

    [Fact]
    public void Resolve_ShortFileName_IsNotTruncated()
    {
        var resolved = ResolvePackageWithFile(
            "C:/build/app.exe", "app.exe", KnownFolder.ProgramFiles / "App");

        // Short name should not be truncated - verify full format C_{sanitized}_{hash}
        var id = resolved.Components[0].Id;
        Assert.StartsWith("C_", id);
        Assert.Contains("app.exe", id);
        Assert.True(id.Length < 72, $"Short ID should be under 72 chars, was {id.Length}");
    }

    [Fact]
    public void Resolve_SpecialCharsInFileName_AreSanitizedToUnderscore()
    {
        var fs = new MockFileSystem();
        // File with special chars that should be sanitized
        fs.AddFile("C:/build/my-app (v2).dll", size: 100);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/my-app (v2).dll").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        var id = result.Value.Components[0].Id;
        // Parentheses and spaces should be replaced with underscores
        Assert.DoesNotContain("(", id);
        Assert.DoesNotContain(")", id);
        Assert.DoesNotContain(" ", id);
    }

    [Fact]
    public void Resolve_DifferentDirectories_ProduceDifferentComponentIds()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/shared.dll", size: 100);

        var package1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/shared.dll").To(KnownFolder.ProgramFiles / "App1"));
        });

        var package2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("C:/build/shared.dll").To(KnownFolder.ProgramFiles / "App2"));
        });

        var resolver = new ComponentResolver(fs);
        var result1 = resolver.Resolve(package1);
        var result2 = resolver.Resolve(package2);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotEqual(result1.Value.Components[0].Id, result2.Value.Components[0].Id);
    }

    [Fact]
    public void Resolve_DifferentFiles_SameDir_ProduceDifferentComponentIds()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/file1.dll", size: 100);
        fs.AddFile("C:/build/file2.dll", size: 200);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("C:/build/file1.dll")
                .Add("C:/build/file2.dll")
                .To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Components.Count);
        Assert.NotEqual(result.Value.Components[0].Id, result.Value.Components[1].Id);
    }

    [Fact]
    public void Resolve_DirectoryHarvest_FileNamesAreExtracted()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/readme.txt", size: 50);
        fs.AddFile("C:/output/app.exe", size: 1000);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/output").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        var fileNames = result.Value.Files.Select(f => f.FileName).OrderBy(n => n).ToList();
        Assert.Contains("app.exe", fileNames);
        Assert.Contains("readme.txt", fileNames);
    }

    [Fact]
    public void Resolve_DirectoryHarvest_EachFileHasOneComponent()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/a.dll", size: 100);
        fs.AddFile("C:/output/b.dll", size: 200);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/output").To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Components, c =>
        {
            Assert.Single(c.Files);
            Assert.Equal(c.KeyPath, c.Files[0].FileId);
        });
    }

    [Fact]
    public void Resolve_DirectoryHarvest_SubdirFiles_TargetDirIncludesSubdir()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/lib/helper.dll", size: 300);

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
        Assert.Equal("helper.dll", file.FileName);
        Assert.Contains("lib", file.TargetDirectory.RelativePath);

        var component = result.Value.Components[0];
        Assert.Contains("lib", component.Directory.RelativePath);
    }

    [Fact]
    public void Resolve_DirectoryHarvest_RootFiles_TargetDirIsBase()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/root.exe", size: 100);

        var baseDir = KnownFolder.ProgramFiles / "MyApp";
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/output").To(baseDir));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        var file = result.Value.Files[0];
        Assert.Equal("root.exe", file.FileName);
        Assert.Equal(baseDir, file.TargetDirectory);
    }

    [Fact]
    public void Resolve_DirectoryHarvest_NeverOverwrite_PropagatedToAllComponents()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/a.dll", size: 100);
        fs.AddFile("C:/output/b.dll", size: 200);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/output").NeverOverwrite().To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Components, c => Assert.True(c.NeverOverwrite));
    }

    [Fact]
    public void Resolve_DirectoryHarvest_Permanent_PropagatedToAllComponents()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/a.dll", size: 100);
        fs.AddFile("C:/output/b.dll", size: 200);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.FromDirectory("C:/output").Permanent().To(KnownFolder.ProgramFiles / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Components, c => Assert.True(c.Permanent));
    }

    [Fact]
    public void Resolve_DefaultFlags_NeverOverwriteAndPermanentAreFalse()
    {
        var resolved = ResolvePackageWithFile(
            "C:/build/app.exe", "app.exe", KnownFolder.ProgramFiles / "App");

        Assert.False(resolved.Components[0].NeverOverwrite);
        Assert.False(resolved.Components[0].Permanent);
    }

    [Fact]
    public void Resolve_NoFeatureRef_IsNull()
    {
        var resolved = ResolvePackageWithFile(
            "C:/build/app.exe", "app.exe", KnownFolder.ProgramFiles / "App");

        Assert.Null(resolved.Components[0].FeatureRef);
    }

    [Fact]
    public void Resolve_StableHash_IsDeterministic()
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
        var r1 = resolver.Resolve(package);
        var r2 = resolver.Resolve(package);

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(r1.Value.Components[0].Id, r2.Value.Components[0].Id);
        Assert.Equal(r1.Value.Files[0].FileId, r2.Value.Files[0].FileId);
    }

    [Fact]
    public void Resolve_SourcePath_IsFullPath()
    {
        var resolved = ResolvePackageWithFile(
            "C:/build/app.exe", "app.exe", KnownFolder.ProgramFiles / "App");

        // SourcePath should be a full path
        Assert.True(Path.IsPathFullyQualified(resolved.Files[0].SourcePath),
            $"Expected full path but got: {resolved.Files[0].SourcePath}");
    }
}
