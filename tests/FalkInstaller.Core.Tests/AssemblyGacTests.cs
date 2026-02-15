using FalkInstaller.Builders;
using FalkInstaller.Models;
using FalkInstaller.Testing;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class AssemblyGacTests
{
    [Fact]
    public void AssemblyBuilder_SetsFileRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a.FileRef("MyLib.dll"));
        });

        Assert.Single(package.Assemblies);
        Assert.Equal("MyLib.dll", package.Assemblies[0].FileRef);
    }

    [Fact]
    public void AssemblyBuilder_DefaultType_IsDotNetAssembly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a.FileRef("MyLib.dll"));
        });

        Assert.Equal(AssemblyType.DotNetAssembly, package.Assemblies[0].Type);
    }

    [Fact]
    public void AssemblyBuilder_DefaultApplicationFileRef_IsNull_MeansGac()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a.FileRef("MyLib.dll"));
        });

        Assert.Null(package.Assemblies[0].ApplicationFileRef);
    }

    [Fact]
    public void AssemblyBuilder_Private_SetsApplicationFileRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a
                .FileRef("MyLib.dll")
                .Private("app.exe"));
        });

        Assert.Equal("app.exe", package.Assemblies[0].ApplicationFileRef);
    }

    [Fact]
    public void AssemblyBuilder_AllFluentMethods_SetProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a
                .FileRef("MyLib.dll")
                .Type(AssemblyType.Win32Assembly)
                .Name("MyLib")
                .Version("1.0.0.0")
                .Culture("neutral")
                .PublicKeyToken("b77a5c561934e089")
                .Architecture("MSIL"));
        });

        var asm = package.Assemblies[0];
        Assert.Equal("MyLib.dll", asm.FileRef);
        Assert.Equal(AssemblyType.Win32Assembly, asm.Type);
        Assert.Equal("MyLib", asm.AssemblyName);
        Assert.Equal("1.0.0.0", asm.AssemblyVersion);
        Assert.Equal("neutral", asm.AssemblyCulture);
        Assert.Equal("b77a5c561934e089", asm.AssemblyPublicKeyToken);
        Assert.Equal("MSIL", asm.ProcessorArchitecture);
    }

    [Fact]
    public void PackageBuilder_MultipleAssemblies_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a.FileRef("Lib1.dll").Name("Lib1").PublicKeyToken("abc123"));
            p.GacAssembly(a => a.FileRef("Lib2.dll").Name("Lib2").PublicKeyToken("def456"));
        });

        Assert.Equal(2, package.Assemblies.Count);
        Assert.Equal("Lib1.dll", package.Assemblies[0].FileRef);
        Assert.Equal("Lib2.dll", package.Assemblies[1].FileRef);
    }

    [Fact]
    public void Validate_MissingFileRef_ProducesASM001Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Assemblies = [new AssemblyModel { FileRef = "" }],
            Features = [new FeatureModel
            {
                Id = "Complete",
                Title = "Complete",
                IsRequired = true,
                IsDefault = true
            }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "ASM001");
    }

    [Fact]
    public void Validate_GacWithoutPublicKeyToken_ProducesASM002Warning()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a
                .FileRef("MyLib.dll")
                .Name("MyLib")
                .Version("1.0.0.0"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "ASM002");
    }

    [Fact]
    public void Validate_PrivateAssemblyWithoutPublicKeyToken_NoASM002Warning()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a
                .FileRef("MyLib.dll")
                .Private("app.exe")
                .Name("MyLib"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "ASM002");
    }

    [Fact]
    public void Validate_InvalidVersionFormat_ProducesASM003Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a
                .FileRef("MyLib.dll")
                .PublicKeyToken("abc123")
                .Version("1.0.0"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "ASM003");
    }

    [Fact]
    public void Validate_ValidVersionFormat_NoASM003Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a
                .FileRef("MyLib.dll")
                .PublicKeyToken("abc123")
                .Version("1.0.0.0"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.Code == "ASM003");
    }

    [Fact]
    public void Validate_GacWithPublicKeyToken_NoASM002Warning()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.GacAssembly(a => a
                .FileRef("MyLib.dll")
                .PublicKeyToken("b77a5c561934e089"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "ASM002");
    }
}
