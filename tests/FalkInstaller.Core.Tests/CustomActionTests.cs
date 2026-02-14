using FalkInstaller.Builders;
using FalkInstaller.Models;
using FalkInstaller.Testing;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class CustomActionTests
{
    [Fact]
    public void CustomActionBuilder_DllFromBinary_SetsTypeAndSource()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Install", ca =>
            {
                ca.DllFromBinary("MyDll", "InstallEntryPoint");
            });
        });

        Assert.Single(package.CustomActions);
        Assert.Equal("CA_Install", package.CustomActions[0].Id);
        Assert.Equal(CustomActionType.DllFromBinary, package.CustomActions[0].Type);
        Assert.Equal("MyDll", package.CustomActions[0].SourceRef);
        Assert.Equal("InstallEntryPoint", package.CustomActions[0].Target);
    }

    [Fact]
    public void CustomActionBuilder_ExeFromBinary_SetsTypeAndSource()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_RunExe", ca =>
            {
                ca.ExeFromBinary("MyExe");
            });
        });

        Assert.Single(package.CustomActions);
        Assert.Equal(CustomActionType.ExeFromBinary, package.CustomActions[0].Type);
        Assert.Equal("MyExe", package.CustomActions[0].SourceRef);
    }

    [Fact]
    public void CustomActionBuilder_SetProperty_SetsTypeSourceAndTarget()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_SetProp", ca =>
            {
                ca.SetProperty("INSTALLDIR", "[ProgramFilesFolder]MyApp");
            });
        });

        Assert.Single(package.CustomActions);
        Assert.Equal(CustomActionType.SetProperty, package.CustomActions[0].Type);
        Assert.Equal("INSTALLDIR", package.CustomActions[0].SourceRef);
        Assert.Equal("[ProgramFilesFolder]MyApp", package.CustomActions[0].Target);
    }

    [Fact]
    public void CustomActionBuilder_SetsCondition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Cond", ca =>
            {
                ca.SetProperty("MY_PROP", "value");
                ca.Condition = "NOT Installed";
            });
        });

        Assert.Equal("NOT Installed", package.CustomActions[0].Condition);
    }

    [Fact]
    public void CustomActionBuilder_SetsAfter()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_After", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry");
                ca.After = "InstallFiles";
            });
        });

        Assert.Equal("InstallFiles", package.CustomActions[0].After);
    }

    [Fact]
    public void CustomActionBuilder_SetsBefore()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Before", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry");
                ca.Before = "InstallFinalize";
            });
        });

        Assert.Equal("InstallFinalize", package.CustomActions[0].Before);
    }

    [Fact]
    public void CustomActionBuilder_SetsSequence()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Seq", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry");
                ca.Sequence = 4500;
            });
        });

        Assert.Equal(4500, package.CustomActions[0].Sequence);
    }

    [Fact]
    public void PackageBuilder_MultipleCustomActions_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_1", ca => ca.SetProperty("P1", "v1"));
            p.CustomAction("CA_2", ca => ca.SetProperty("P2", "v2"));
            p.CustomAction("CA_3", ca => ca.DllFromBinary("Dll", "Entry"));
        });

        Assert.Equal(3, package.CustomActions.Count);
    }

    [Fact]
    public void PackageBuilder_Binary_AddsBinaryToModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Binary("MyDll", @"C:\build\custom.dll");
        });

        Assert.Single(package.Binaries);
        Assert.Equal("MyDll", package.Binaries[0].Name);
        Assert.Equal(@"C:\build\custom.dll", package.Binaries[0].SourcePath);
    }

    [Fact]
    public void PackageBuilder_MultipleBinaries_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Binary("Bin1", @"C:\build\bin1.dll");
            p.Binary("Bin2", @"C:\build\bin2.exe");
        });

        Assert.Equal(2, package.Binaries.Count);
        Assert.Equal("Bin1", package.Binaries[0].Name);
        Assert.Equal("Bin2", package.Binaries[1].Name);
    }

    [Fact]
    public void Validate_CustomActionWithEmptyId_ProducesCA001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomActions =
            [
                new CustomActionModel
                {
                    Id = "",
                    Type = CustomActionType.SetProperty,
                    SourceRef = "PROP"
                }
            ],
            Features =
            [
                new FeatureModel
                {
                    Id = "Complete",
                    Title = "Complete",
                    IsRequired = true,
                    IsDefault = true
                }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CA001");
    }

    [Fact]
    public void Validate_CustomActionWithZeroType_ProducesCA002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomActions =
            [
                new CustomActionModel
                {
                    Id = "CA_Test",
                    Type = 0,
                    SourceRef = "Something"
                }
            ],
            Features =
            [
                new FeatureModel
                {
                    Id = "Complete",
                    Title = "Complete",
                    IsRequired = true,
                    IsDefault = true
                }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CA002");
    }

    [Fact]
    public void Validate_CustomActionWithEmptySourceRef_ProducesCA003()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CustomActions =
            [
                new CustomActionModel
                {
                    Id = "CA_Test",
                    Type = CustomActionType.DllFromBinary,
                    SourceRef = ""
                }
            ],
            Features =
            [
                new FeatureModel
                {
                    Id = "Complete",
                    Title = "Complete",
                    IsRequired = true,
                    IsDefault = true
                }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CA003");
    }

    [Fact]
    public void Validate_ValidCustomAction_NoErrors()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Test", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry");
                ca.After = "InstallFiles";
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("CA0"));
    }

    [Fact]
    public void CustomActionType_Constants_HaveCorrectValues()
    {
        Assert.Equal(1, CustomActionType.DllFromBinary);
        Assert.Equal(2, CustomActionType.ExeFromBinary);
        Assert.Equal(5, CustomActionType.JScriptFromBinary);
        Assert.Equal(6, CustomActionType.VBScriptFromBinary);
        Assert.Equal(34, CustomActionType.ExeInDir);
        Assert.Equal(51, CustomActionType.SetProperty);
        Assert.Equal(35, CustomActionType.SetDirectory);
    }

    [Fact]
    public void BinaryModel_SetsNameAndSourcePath()
    {
        var binary = new BinaryModel
        {
            Name = "TestBinary",
            SourcePath = @"C:\path\to\binary.dll"
        };

        Assert.Equal("TestBinary", binary.Name);
        Assert.Equal(@"C:\path\to\binary.dll", binary.SourcePath);
    }
}
