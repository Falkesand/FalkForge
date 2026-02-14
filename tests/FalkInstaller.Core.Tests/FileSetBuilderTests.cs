using FalkInstaller.Builders;
using FalkInstaller.Testing;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class FileSetBuilderTests
{
    [Fact]
    public void Add_CreatesFileEntryWithCorrectName()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("C:\\build\\output\\myapp.exe")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        Assert.Single(package.Files);
        Assert.Equal("myapp.exe", package.Files[0].FileName);
        Assert.Equal("C:\\build\\output\\myapp.exe", package.Files[0].SourcePath);
    }

    [Fact]
    public void FromDirectory_CreatesWildcardEntry()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .FromDirectory("C:\\build\\output")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        Assert.Single(package.Files);
        Assert.Equal("*", package.Files[0].FileName);
        Assert.Equal("C:\\build\\output", package.Files[0].SourcePath);
    }

    [Fact]
    public void To_SetsTargetDirectory()
    {
        var target = KnownFolder.ProgramFiles / "Corp" / "App";

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("app.exe")
                .To(target));
        });

        Assert.Equal(target, package.Files[0].TargetDirectory);
    }

    [Fact]
    public void Build_WithoutTo_ThrowsInvalidOperationException()
    {
        var builder = new FileSetBuilder();
        builder.Add("app.exe");

        // FileSetBuilder.Build() is internal, so we trigger it through PackageBuilder
        // which calls builder.Build() internally
        Assert.Throws<InvalidOperationException>(() =>
        {
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.Files(f => f.Add("app.exe"));
            });
        });
    }

    [Fact]
    public void MultipleFiles_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("app.exe")
                .Add("config.json")
                .Add("readme.txt")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        Assert.Equal(3, package.Files.Count);
        Assert.Equal("app.exe", package.Files[0].FileName);
        Assert.Equal("config.json", package.Files[1].FileName);
        Assert.Equal("readme.txt", package.Files[2].FileName);
    }

    [Fact]
    public void FirstFile_IsKeyPath()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("app.exe")
                .Add("lib.dll")
                .To(KnownFolder.ProgramFiles / "App"));
        });

        Assert.True(package.Files[0].IsKeyPath);
        Assert.False(package.Files[1].IsKeyPath);
    }

    [Fact]
    public void MultipleFileSets_AreAccumulated()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "App"));
            p.Files(f => f.Add("data.db").To(KnownFolder.CommonAppData / "App"));
        });

        Assert.Equal(2, package.Files.Count);
    }

    [Fact]
    public void FromDirectory_SetsIsKeyPathOnFirstOnly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .FromDirectory("C:\\output")
                .Add("extra.dll")
                .To(KnownFolder.ProgramFiles / "App"));
        });

        // FromDirectory is first, so it's key path
        Assert.True(package.Files[0].IsKeyPath);
        // extra.dll is second
        Assert.False(package.Files[1].IsKeyPath);
    }
}
