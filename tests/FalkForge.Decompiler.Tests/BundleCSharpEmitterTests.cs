using FalkForge.Compiler.Bundle;
using Xunit;

namespace FalkForge.Decompiler.Tests;

public sealed class BundleCSharpEmitterTests
{
    private static readonly Guid TestBundleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestUpgradeCode = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Emit_EmptyBundle_ContainsBuilderStructure()
    {
        var model = CreateMinimalBundle();

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("Installer.BuildBundle(b =>", source);
        Assert.Contains("});", source);
    }

    [Fact]
    public void Emit_TopLevelFields_EmitsNameManufacturerVersion()
    {
        var model = CreateMinimalBundle();

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.Name(\"Test Bundle\");", source);
        Assert.Contains("b.Manufacturer(\"Test Corp\");", source);
        Assert.Contains("b.Version(\"1.0.0\");", source);
    }

    [Fact]
    public void Emit_TopLevelFields_EmitsBundleIdAndUpgradeCode()
    {
        var model = CreateMinimalBundle();

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains($"b.BundleId(new Guid(\"{TestBundleId}\"));", source);
        Assert.Contains($"b.UpgradeCode(new Guid(\"{TestUpgradeCode}\"));", source);
    }

    [Fact]
    public void Emit_ScopePerMachine_DoesNotEmitScope()
    {
        var model = CreateMinimalBundle(scope: InstallScope.PerMachine);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.DoesNotContain("b.Scope(", source);
    }

    [Fact]
    public void Emit_ScopePerUser_EmitsScope()
    {
        var model = CreateMinimalBundle(scope: InstallScope.PerUser);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.Scope(InstallScope.PerUser);", source);
    }

    [Fact]
    public void Emit_SingleMsiPackage_EmitsMsiPackageCall()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "MyApp",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "My Application",
                SourcePath = "MyApp.msi",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.MsiPackage(\"MyApp.msi\", p =>", source);
        Assert.Contains("p.DisplayName(\"My Application\");", source);
    }

    [Fact]
    public void Emit_SingleExePackage_EmitsExePackageCall()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "setup",
                Type = BundlePackageType.ExePackage,
                DisplayName = "Setup Tool",
                SourcePath = "setup.exe",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.ExePackage(\"setup.exe\", p =>", source);
        Assert.Contains("p.DisplayName(\"Setup Tool\");", source);
    }

    [Fact]
    public void Emit_MsuPackage_EmitsMsuPackageCall()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "KB12345",
                Type = BundlePackageType.MsuPackage,
                DisplayName = "Security Update",
                SourcePath = "update.msu",
                KbArticle = "KB12345",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.MsuPackage(\"update.msu\", p =>", source);
        Assert.Contains("p.Id(\"KB12345\");", source);
        Assert.Contains("p.DisplayName(\"Security Update\");", source);
        Assert.Contains("p.KbArticle(\"KB12345\");", source);
    }

    [Fact]
    public void Emit_MspPackage_EmitsMspPackageCall()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "patch1",
                Type = BundlePackageType.MspPackage,
                DisplayName = "Hotfix",
                SourcePath = "hotfix.msp",
                PatchCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
                TargetProductCode = "{11111111-2222-3333-4444-555555555555}",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.MspPackage(\"hotfix.msp\", p =>", source);
        Assert.Contains("p.Id(\"patch1\");", source);
        Assert.Contains("p.DisplayName(\"Hotfix\");", source);
        Assert.Contains("p.PatchCode(\"{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}\");", source);
        Assert.Contains("p.TargetProductCode(\"{11111111-2222-3333-4444-555555555555}\");", source);
    }

    [Fact]
    public void Emit_BundlePackage_EmitsBundlePackageCall()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "nested",
                Type = BundlePackageType.BundlePackage,
                DisplayName = "Nested Installer",
                SourcePath = "nested.exe",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.BundlePackage(\"nested.exe\", p =>", source);
        Assert.Contains("p.DisplayName(\"Nested Installer\");", source);
    }

    [Fact]
    public void Emit_NetRuntime_EmitsNetRuntimeCall()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "dotnet8",
                Type = BundlePackageType.NetRuntime,
                DisplayName = ".NET 8 Runtime",
                SourcePath = "dotnet-runtime-8.0.0-win-x64.exe",
                Version = "8.0.0",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.NetRuntime(\"dotnet-runtime-8.0.0-win-x64.exe\", p =>", source);
        Assert.Contains("p.Id(\"dotnet8\");", source);
        Assert.Contains("p.DisplayName(\".NET 8 Runtime\");", source);
        Assert.Contains("p.Version(\"8.0.0\");", source);
    }

    [Fact]
    public void Emit_PackageWithProperties_EmitsProperties()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "MyApp",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "MyApp",
                SourcePath = "MyApp.msi",
                Properties = new Dictionary<string, string>
                {
                    ["INSTALLFOLDER"] = @"C:\Program Files\MyApp",
                    ["ADDLOCAL"] = "ALL"
                }
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("p.Property(\"INSTALLFOLDER\", \"C:\\\\Program Files\\\\MyApp\");", source);
        Assert.Contains("p.Property(\"ADDLOCAL\", \"ALL\");", source);
    }

    [Fact]
    public void Emit_PackageWithInstallCondition_EmitsCondition()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "MyApp",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "MyApp",
                SourcePath = "MyApp.msi",
                InstallCondition = "VersionNT64"
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("p.InstallCondition(\"VersionNT64\");", source);
    }

    [Fact]
    public void Emit_PackageWithVersion_EmitsVersion()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "MyApp",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "MyApp",
                SourcePath = "MyApp.msi",
                Version = "2.5.0"
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("p.Version(\"2.5.0\");", source);
    }

    [Fact]
    public void Emit_NonVitalPackage_EmitsVitalFalse()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "optional",
                Type = BundlePackageType.ExePackage,
                DisplayName = "optional",
                SourcePath = "optional.exe",
                Vital = false
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("p.Vital(false);", source);
    }

    [Fact]
    public void Emit_VitalPackageDefault_DoesNotEmitVital()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "app",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "App",
                SourcePath = "app.msi",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.DoesNotContain("p.Vital(true)", source);
    }

    [Fact]
    public void Emit_RollbackBoundary_EmitsRollbackBoundary()
    {
        var model = CreateMinimalBundle(chain:
        [
            new RollbackBoundaryChainItem(new RollbackBoundaryModel
            {
                Id = "rb1"
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.RollbackBoundary(\"rb1\");", source);
    }

    [Fact]
    public void Emit_NonVitalRollbackBoundary_EmitsVitalFalse()
    {
        var model = CreateMinimalBundle(chain:
        [
            new RollbackBoundaryChainItem(new RollbackBoundaryModel
            {
                Id = "rb_optional",
                Vital = false
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.RollbackBoundary(\"rb_optional\", rb => rb.Vital(false));", source);
    }

    [Fact]
    public void Emit_RelatedBundle_DefaultRelation_EmitsWithoutConfigure()
    {
        var model = CreateMinimalBundle(relatedBundles:
        [
            new RelatedBundleModel
            {
                BundleId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
                Relation = RelatedBundleRelation.Upgrade
            }
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.RelatedBundle(\"{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}\");", source);
        Assert.DoesNotContain("RelatedBundleRelation.Upgrade", source);
    }

    [Fact]
    public void Emit_RelatedBundle_NonDefaultRelation_EmitsRelation()
    {
        var model = CreateMinimalBundle(relatedBundles:
        [
            new RelatedBundleModel
            {
                BundleId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
                Relation = RelatedBundleRelation.Addon
            }
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.RelatedBundle(\"{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}\", r => r.Relation(RelatedBundleRelation.Addon));", source);
    }

    [Fact]
    public void Emit_Container_EmitsContainerCall()
    {
        var model = CreateMinimalBundle(containers:
        [
            new ContainerModel { Id = "MainContainer" }
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.Container(\"MainContainer\");", source);
    }

    [Fact]
    public void Emit_UiConfigBuiltInWithLicense_EmitsUseBuiltInUI()
    {
        var model = CreateMinimalBundle(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.BuiltIn,
            LicenseFile = "license.rtf"
        });

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.UseBuiltInUI(licenseFile: \"license.rtf\");", source);
    }

    [Fact]
    public void Emit_UiConfigBuiltInNoArgs_EmitsUseBuiltInUI()
    {
        var model = CreateMinimalBundle(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.BuiltIn
        });

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.UseBuiltInUI();", source);
    }

    [Fact]
    public void Emit_UiConfigSilent_EmitsUseSilentUI()
    {
        var model = CreateMinimalBundle(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.Silent
        });

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.UseSilentUI();", source);
    }

    [Fact]
    public void Emit_UiConfigCustom_EmitsUseCustomUIWithTodo()
    {
        var model = CreateMinimalBundle(uiConfig: new BundleUiConfig
        {
            UiType = BundleUiType.Custom,
            CustomUiProjectPath = "MyUiProject"
        });

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("b.UseCustomUI(\"TODO: set UI project path\");", source);
    }

    [Fact]
    public void Emit_MixedChain_EmitsInOrder()
    {
        var model = CreateMinimalBundle(chain:
        [
            new RollbackBoundaryChainItem(new RollbackBoundaryModel { Id = "rb1" }),
            new PackageChainItem(new BundlePackageModel
            {
                Id = "MyApp",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "My Application",
                SourcePath = "MyApp.msi"
            }),
            new PackageChainItem(new BundlePackageModel
            {
                Id = "runtime",
                Type = BundlePackageType.ExePackage,
                DisplayName = "Runtime Setup",
                SourcePath = "runtime.exe",
                Vital = false
            }),
            new RollbackBoundaryChainItem(new RollbackBoundaryModel { Id = "rb2" }),
            new PackageChainItem(new BundlePackageModel
            {
                Id = "update",
                Type = BundlePackageType.MsuPackage,
                DisplayName = "update",
                SourcePath = "update.msu",
                KbArticle = "KB99999"
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        var rbPos = source.IndexOf("c.RollbackBoundary(\"rb1\")");
        var msiPos = source.IndexOf("c.MsiPackage(\"MyApp.msi\"");
        var exePos = source.IndexOf("c.ExePackage(\"runtime.exe\"");
        var rb2Pos = source.IndexOf("c.RollbackBoundary(\"rb2\")");
        var msuPos = source.IndexOf("c.MsuPackage(\"update.msu\"");

        Assert.True(rbPos > 0, "RollbackBoundary rb1 not found");
        Assert.True(msiPos > rbPos, "MsiPackage should come after rb1");
        Assert.True(exePos > msiPos, "ExePackage should come after MsiPackage");
        Assert.True(rb2Pos > exePos, "RollbackBoundary rb2 should come after ExePackage");
        Assert.True(msuPos > rb2Pos, "MsuPackage should come after rb2");
        Assert.Contains("p.Vital(false);", source);
        Assert.Contains("p.KbArticle(\"KB99999\");", source);
    }

    [Fact]
    public void Emit_InformationLossComment_PresentInOutput()
    {
        var model = CreateMinimalBundle();

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("// Decompiled from bundle: Test Bundle", source);
        Assert.Contains("// NOTE: Some information is lost during decompilation:", source);
        Assert.Contains("//   - UI configuration (logo, theme, watermark, banner) is not preserved", source);
        Assert.Contains("//   - Container download URLs are not preserved", source);
        Assert.Contains("//   - Custom UI project paths are not preserved", source);
    }

    [Fact]
    public void Emit_IncludesUsingStatements()
    {
        var model = CreateMinimalBundle();

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("using FalkForge;", source);
        Assert.Contains("using FalkForge.Compiler.Bundle.Builders;", source);
    }

    [Fact]
    public void Emit_EscapesSpecialCharacters()
    {
        var model = CreateMinimalBundle();
        model = new BundleModel
        {
            Name = "Test \"Bundle\"",
            Manufacturer = "Test\\Corp",
            Version = "1.0.0",
            BundleId = TestBundleId,
            UpgradeCode = TestUpgradeCode,
            Scope = InstallScope.PerMachine,
            Packages = []
        };

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("Test \\\"Bundle\\\"", source);
        Assert.Contains("Test\\\\Corp", source);
    }

    [Fact]
    public void Emit_PackageWithRemotePayload_EmitsRemotePayload()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "remote",
                Type = BundlePackageType.ExePackage,
                DisplayName = "Remote Package",
                SourcePath = "remote.exe",
                RemotePayload = new RemotePayloadModel
                {
                    DownloadUrl = "https://example.com/remote.exe",
                    Sha256Hash = "abc123def456",
                    Size = 1048576
                }
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("p.RemotePayload(\"https://example.com/remote.exe\", \"abc123def456\", 1048576);", source);
    }

    [Fact]
    public void Emit_PackageWithExitCodes_EmitsExitCodes()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "app",
                Type = BundlePackageType.ExePackage,
                DisplayName = "app",
                SourcePath = "app.exe",
                ExitCodes = new Dictionary<int, ExitCodeBehavior>
                {
                    [3010] = ExitCodeBehavior.RebootRequired,
                    [1641] = ExitCodeBehavior.ScheduleReboot
                }
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("p.ExitCode(3010, ExitCodeBehavior.RebootRequired);", source);
        Assert.Contains("p.ExitCode(1641, ExitCodeBehavior.ScheduleReboot);", source);
    }

    [Fact]
    public void Emit_PackageWithContainerId_EmitsContainer()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "app",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "app",
                SourcePath = "app.msi",
                ContainerId = "MainContainer"
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("p.Container(\"MainContainer\");", source);
    }

    [Fact]
    public void Emit_PackageWithDefaultIdAndDisplayName_OmitsBoth()
    {
        var model = CreateMinimalBundle(chain:
        [
            new PackageChainItem(new BundlePackageModel
            {
                Id = "app",
                Type = BundlePackageType.MsiPackage,
                DisplayName = "app",
                SourcePath = "app.msi",
                Vital = true
            })
        ]);

        var source = BundleCSharpEmitter.Emit(model);

        Assert.Contains("c.MsiPackage(\"app.msi\", p => { });", source);
        Assert.DoesNotContain("p.Id(", source);
        Assert.DoesNotContain("p.DisplayName(", source);
    }

    [Fact]
    public void Emit_EmptyChain_DoesNotEmitChainBlock()
    {
        var model = CreateMinimalBundle();

        var source = BundleCSharpEmitter.Emit(model);

        Assert.DoesNotContain("b.Chain(", source);
    }

    private static BundleModel CreateMinimalBundle(
        InstallScope scope = InstallScope.PerMachine,
        IReadOnlyList<ChainItem>? chain = null,
        IReadOnlyList<RelatedBundleModel>? relatedBundles = null,
        IReadOnlyList<ContainerModel>? containers = null,
        BundleUiConfig? uiConfig = null)
    {
        return new BundleModel
        {
            Name = "Test Bundle",
            Manufacturer = "Test Corp",
            Version = "1.0.0",
            BundleId = TestBundleId,
            UpgradeCode = TestUpgradeCode,
            Scope = scope,
            Packages = [],
            Chain = chain ?? [],
            RelatedBundles = relatedBundles ?? [],
            Containers = containers ?? [],
            UiConfig = uiConfig
        };
    }
}
