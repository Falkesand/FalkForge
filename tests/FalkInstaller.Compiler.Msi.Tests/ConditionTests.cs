using System.Runtime.Versioning;
using FalkInstaller.Models;
using FalkInstaller.Platform.Windows;
using FalkInstaller.Testing;
using Xunit;

namespace FalkInstaller.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class ConditionTests
{
    [Fact]
    public void ComponentResolver_PropagatesCondition_FromFileEntry()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/optional.dll", size: 1024);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("C:/build/optional.dll")
                .ComponentCondition("INSTALL_OPTIONAL")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Components);
        Assert.Equal("INSTALL_OPTIONAL", result.Value.Components[0].Condition);
    }

    [Fact]
    public void ComponentResolver_NoCondition_IsNull()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/build/app.exe", size: 1024);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("C:/build/app.exe")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Components);
        Assert.Null(result.Value.Components[0].Condition);
    }

    [Fact]
    public void ComponentResolver_DirectoryHarvest_PropagatesCondition()
    {
        var fs = new MockFileSystem();
        fs.AddFile("C:/output/a.dll", size: 100);
        fs.AddFile("C:/output/b.dll", size: 200);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .FromDirectory("C:/output")
                .ComponentCondition("INSTALL_LIBS")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        var resolver = new ComponentResolver(fs);
        var result = resolver.Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Components.Count);
        Assert.All(result.Value.Components, c => Assert.Equal("INSTALL_LIBS", c.Condition));
    }

    [Fact]
    public void Integration_ConditionTable_Emitted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"CondTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "CondApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "CondApp"));
                p.Feature("Optional", f =>
                {
                    f.Title = "Optional Feature";
                    f.Condition("NOT REMOVE", 0);
                    f.Condition("PREMIUM_LICENSE", 1);
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
            Assert.True(File.Exists(result.Value), "MSI file not found");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Integration_ComponentCondition_Emitted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"CompCondTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "optional.dll");
            File.WriteAllText(sourceFile, "fake content");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "CompCondApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f
                    .Add(sourceFile)
                    .ComponentCondition("INSTALL_OPTIONAL")
                    .To(KnownFolder.ProgramFiles / "Corp" / "CompCondApp"));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
            Assert.True(File.Exists(result.Value), "MSI file not found");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
