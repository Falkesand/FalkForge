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

    // --- Task 4F: Custom Action Rollback/Commit Scheduling Tests ---

    [Fact]
    public void CustomActionType_SchedulingFlags_HaveCorrectValues()
    {
        Assert.Equal(0x040, CustomActionType.Continue);
        Assert.Equal(0x100, CustomActionType.InScript);
        Assert.Equal(0x200, CustomActionType.Rollback);
        Assert.Equal(0x400, CustomActionType.Commit);
        Assert.Equal(0x800, CustomActionType.NoImpersonate);
    }

    [Fact]
    public void Deferred_SetsInScriptBit()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Deferred", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry").Deferred();
            });
        });

        var type = package.CustomActions[0].Type;
        Assert.Equal(CustomActionType.DllFromBinary | CustomActionType.InScript, type);
        Assert.Equal(0x101, type);
    }

    [Fact]
    public void Rollback_SetsInScriptAndRollbackBits()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Rollback", ca =>
            {
                ca.DllFromBinary("MyDll", "RollbackEntry").Rollback();
            });
        });

        var type = package.CustomActions[0].Type;
        Assert.Equal(CustomActionType.DllFromBinary | CustomActionType.InScript | CustomActionType.Rollback, type);
        Assert.Equal(0x301, type);
    }

    [Fact]
    public void Commit_SetsInScriptAndCommitBits()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Commit", ca =>
            {
                ca.DllFromBinary("MyDll", "CommitEntry").Commit();
            });
        });

        var type = package.CustomActions[0].Type;
        Assert.Equal(CustomActionType.DllFromBinary | CustomActionType.InScript | CustomActionType.Commit, type);
        Assert.Equal(0x501, type);
    }

    [Fact]
    public void NoImpersonate_SetsNoImpersonateBit()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_NoImpersonate", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry").Deferred().NoImpersonate();
            });
        });

        var type = package.CustomActions[0].Type;
        Assert.Equal(CustomActionType.DllFromBinary | CustomActionType.InScript | CustomActionType.NoImpersonate, type);
        Assert.Equal(0x901, type);
    }

    [Fact]
    public void ContinueOnError_SetsContinueBit()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Continue", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry").ContinueOnError();
            });
        });

        var type = package.CustomActions[0].Type;
        Assert.Equal(CustomActionType.DllFromBinary | CustomActionType.Continue, type);
        Assert.Equal(0x041, type);
    }

    [Fact]
    public void DeferredPlusNoImpersonate_CombinesCorrectly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_DeferredNoImp", ca =>
            {
                ca.ExeFromBinary("MyExe").Deferred().NoImpersonate();
            });
        });

        var type = package.CustomActions[0].Type;
        var expected = CustomActionType.ExeFromBinary | CustomActionType.InScript | CustomActionType.NoImpersonate;
        Assert.Equal(expected, type);
        Assert.Equal(0x902, type);
    }

    [Fact]
    public void RollbackPlusNoImpersonatePlusContinue_CombinesCorrectly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_RollbackFull", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry")
                    .Rollback()
                    .NoImpersonate()
                    .ContinueOnError();
            });
        });

        var type = package.CustomActions[0].Type;
        var expected = CustomActionType.DllFromBinary
                     | CustomActionType.InScript
                     | CustomActionType.Rollback
                     | CustomActionType.NoImpersonate
                     | CustomActionType.Continue;
        Assert.Equal(expected, type);
        Assert.Equal(0xB41, type);
    }

    [Fact]
    public void Validate_RollbackAndCommitConflict_ProducesCA004()
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
                    Id = "CA_Conflict",
                    Type = CustomActionType.DllFromBinary
                         | CustomActionType.InScript
                         | CustomActionType.Rollback
                         | CustomActionType.Commit,
                    SourceRef = "MyDll"
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
        Assert.Contains(result.Errors, e => e.Code == "CA004");
    }

    [Fact]
    public void Validate_NoImpersonateWithoutInScript_ProducesCA005Warning()
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
                    Id = "CA_BadNoImp",
                    Type = CustomActionType.DllFromBinary | CustomActionType.NoImpersonate,
                    SourceRef = "MyDll"
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

        Assert.Contains(result.Warnings, w => w.Code == "CA005");
    }

    [Fact]
    public void Validate_DeferredWithNoImpersonate_NoWarnings()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Valid", ca =>
            {
                ca.DllFromBinary("MyDll", "Entry").Deferred().NoImpersonate();
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "CA005");
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("CA0"));
    }

    [Fact]
    public void DeferredCustomAction_IntegrationWithPackageBuilder()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "FullApp";
            p.Manufacturer = "Corp";
            p.Binary("CustomDll", @"C:\build\custom.dll");
            p.CustomAction("CA_SetData", ca =>
            {
                ca.SetProperty("CA_DeferredAction", "SomeData");
            });
            p.CustomAction("CA_DeferredAction", ca =>
            {
                ca.DllFromBinary("CustomDll", "DeferredEntry")
                    .Deferred()
                    .NoImpersonate();
                ca.After = "InstallFiles";
                ca.Condition = "NOT Installed";
            });
            p.CustomAction("CA_RollbackAction", ca =>
            {
                ca.DllFromBinary("CustomDll", "RollbackEntry")
                    .Rollback()
                    .NoImpersonate();
                ca.Before = "CA_DeferredAction";
            });
        });

        Assert.Equal(3, package.CustomActions.Count);
        Assert.Single(package.Binaries);

        // Verify the set-property action is immediate (no flags)
        Assert.Equal(CustomActionType.SetProperty, package.CustomActions[0].Type);

        // Verify deferred action
        var deferred = package.CustomActions[1];
        Assert.Equal(0x901, deferred.Type);
        Assert.Equal("InstallFiles", deferred.After);
        Assert.Equal("NOT Installed", deferred.Condition);

        // Verify rollback action
        var rollback = package.CustomActions[2];
        Assert.Equal(0xB01, rollback.Type);
        Assert.Equal("CA_DeferredAction", rollback.Before);

        // Validate the whole package
        var result = InstallerValidator.Validate(package);
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("CA0"));
    }
}
