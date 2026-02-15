using FalkInstaller.Testing;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class ComponentConditionTests
{
    [Fact]
    public void FileSetBuilder_ComponentCondition_SetsOnModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("optional.dll")
                .ComponentCondition("INSTALL_OPTIONAL")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        Assert.Single(package.Files);
        Assert.Equal("INSTALL_OPTIONAL", package.Files[0].ComponentCondition);
    }

    [Fact]
    public void FileSetBuilder_NoComponentCondition_IsNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("app.exe")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        Assert.Single(package.Files);
        Assert.Null(package.Files[0].ComponentCondition);
    }

    [Fact]
    public void FileSetBuilder_ComponentCondition_AppliedToAllFiles()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("a.dll")
                .Add("b.dll")
                .ComponentCondition("INSTALL_LIBS")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        Assert.Equal(2, package.Files.Count);
        Assert.All(package.Files, file => Assert.Equal("INSTALL_LIBS", file.ComponentCondition));
    }

    [Fact]
    public void FileSetBuilder_DirectoryHarvest_ComponentCondition_AppliedToWildcard()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .FromDirectory("C:\\output")
                .ComponentCondition("INSTALL_ALL")
                .To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        Assert.Single(package.Files);
        Assert.Equal("INSTALL_ALL", package.Files[0].ComponentCondition);
    }
}
