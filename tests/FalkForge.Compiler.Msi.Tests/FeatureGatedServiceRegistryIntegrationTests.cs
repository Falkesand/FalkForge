using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Proves that gating a Windows service or a registry entry to a feature via
/// FeatureBuilder.Service()/.Registry() actually drives the compiled MSI: the
/// resource's synthesized component must appear in the FeatureComponents table
/// under the declaring feature, and NOT under an unrelated sibling feature.
/// FeatureRef alone (an in-memory model field) proves nothing — only the
/// compiled table content does, which is what this test asserts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FeatureGatedServiceRegistryIntegrationTests
{
    [Fact]
    public void Compile_ServiceGatedToFeature_FeatureComponentsMapsServiceUnderThatFeatureOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeatSvc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "svc.exe");
            File.WriteAllText(sourceFile, "fake service exe for feature-gating test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FeatSvcApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("FeatureA", f =>
                {
                    f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatSvcApp"));
                });
                p.Feature("FeatureB", f =>
                {
                    f.Service("AppSvc", s =>
                    {
                        s.Executable = @"C:\payload\completely_unrelated_name.exe";
                        s.DisplayName = "App Service";
                    });
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            // Find the component the ServiceInstall row for "AppSvc" actually attached to.
            var serviceRows = db.QueryRows(
                "SELECT `ServiceInstall`, `Name`, `Component_` FROM `ServiceInstall` WHERE `Name` = 'AppSvc'", 3).Value;
            var serviceComponentId = Assert.Single(serviceRows)[2];
            Assert.NotNull(serviceComponentId);

            var featureComponentRows = db.QueryRows(
                "SELECT `Feature_`, `Component_` FROM `FeatureComponents`", 2).Value;

            Assert.Contains(featureComponentRows,
                r => r[1] == serviceComponentId && r[0] == "FeatureB");
            Assert.DoesNotContain(featureComponentRows,
                r => r[1] == serviceComponentId && r[0] == "FeatureA");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_RegistryGatedToFeature_FeatureComponentsMapsEntryUnderThatFeatureOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeatReg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for registry feature-gating test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FeatRegApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("FeatureA", f =>
                {
                    f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatRegApp"));
                });
                p.Feature("FeatureB", f =>
                {
                    f.Registry(r => r.Key(RegistryRoot.LocalMachine, @"Software\TestCorp\FeatRegApp", k =>
                        k.Value("InstallPath", @"C:\somewhere")));
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var registryRows = db.QueryRows(
                "SELECT `Registry`, `Name`, `Component_` FROM `Registry` WHERE `Name` = 'InstallPath'", 3).Value;
            var registryComponentId = Assert.Single(registryRows)[2];
            Assert.NotNull(registryComponentId);

            var featureComponentRows = db.QueryRows(
                "SELECT `Feature_`, `Component_` FROM `FeatureComponents`", 2).Value;

            Assert.Contains(featureComponentRows,
                r => r[1] == registryComponentId && r[0] == "FeatureB");
            Assert.DoesNotContain(featureComponentRows,
                r => r[1] == registryComponentId && r[0] == "FeatureA");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Resolve_RegistryEntryWithExplicitComponentIdAndFeatureRef_SkipsSynthesis()
    {
        // WHY: an explicit ComponentId is a stronger, user-authored override than a FeatureRef.
        // ComponentResolver must NOT synthesize a feature component for such an entry — the entry's
        // index must be absent from RegistryFeatureComponents so RegistryTableProducer falls
        // through to the explicit ComponentId. ComponentId is not reachable via the fluent API
        // (it is a reserved override), so this is authored at the model level. If the precedence
        // were flipped (synthesis wins), the entry's index would appear in the map and this fails.
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Acme",
            Version = new Version(1, 0, 0),
            RegistryEntries = new[]
            {
                new RegistryEntryModel
                {
                    Root = RegistryRoot.LocalMachine,
                    Key = @"Software\Acme\App",
                    ValueName = "Pinned",
                    Value = "yes",
                    FeatureRef = "FeatureB",
                    ComponentId = "C_ExplicitTarget"
                }
            }
        };

        var result = new ComponentResolver(new MockFileSystem()).Resolve(package);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.RegistryFeatureComponents);
        Assert.Empty(result.Value.Components);
    }

    [Fact]
    public void Produce_RegistryEntryWithExplicitComponentIdAndFeatureRef_RowUsesExplicitComponentId()
    {
        // WHY: RegistryTableProducer must resolve the Component_ FK to the explicit ComponentId,
        // not to a synthesized feature component, when both are set. Guards the producer half of
        // the same precedence rule the resolver half is guarded above.
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Acme",
            Version = new Version(1, 0, 0),
            Features = new[] { new FeatureModel { Id = "FeatureB", Title = "B" } },
            RegistryEntries = new[]
            {
                new RegistryEntryModel
                {
                    Root = RegistryRoot.LocalMachine,
                    Key = @"Software\Acme\App",
                    ValueName = "Pinned",
                    Value = "yes",
                    FeatureRef = "FeatureB",
                    ComponentId = "C_ExplicitTarget"
                }
            }
        };

        var resolved = new ComponentResolver(new MockFileSystem()).Resolve(package).Value;
        var context = new RecipeBuildContext(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());

        var result = new RegistryTableProducer().Produce(context);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        var componentFk = Assert.IsType<CellValue.ForeignKey>(row.Cells[5]);
        Assert.Equal("C_ExplicitTarget", componentFk.TargetKey);
    }

    [Fact]
    public void Compile_FeatureGatedServiceWithCondition_ComponentCarriesCondition()
    {
        // WHY: ServiceBuilder.Condition(...) is a public, documented method. When a service is
        // feature-gated it gets its own synthesized component, so that component must carry the
        // service's ComponentCondition — otherwise the user's condition is silently dropped and
        // the service installs unconditionally.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeatSvcCond_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for service condition test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FeatSvcCondApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("FeatureA", f =>
                {
                    f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatSvcCondApp"));
                });
                p.Feature("FeatureB", f =>
                {
                    f.Service("CondSvc", s =>
                    {
                        s.Executable = @"C:\payload\cond_svc.exe";
                        s.Condition("VersionNT >= 600");
                    });
                });
            });

            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var serviceRows = db.QueryRows(
                "SELECT `Name`, `Component_` FROM `ServiceInstall` WHERE `Name` = 'CondSvc'", 2).Value;
            var serviceComponentId = Assert.Single(serviceRows)[1];

            var componentRows = db.QueryRows(
                "SELECT `Component`, `Condition` FROM `Component`", 2).Value;
            var componentRow = Assert.Single(componentRows, r => r[0] == serviceComponentId);
            Assert.Equal("VersionNT >= 600", componentRow[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Resolve_TwoFeatureGatedServicesWithLongPrefixSharedNames_ProduceDistinctComponentIds()
    {
        // WHY: MSI service names may be long (up to 255 chars). The synthesized component id must
        // stay unique even when two service names share a long prefix — the disambiguating hash
        // must survive the 72-char identifier truncation. If the hash were appended and truncated
        // away, both services would collapse onto one component (one silently in the wrong feature).
        var fs = new MockFileSystem();
        var sharedPrefix = new string('S', 80);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("FeatureA", f =>
            {
                f.Service(sharedPrefix + "AAA", s => s.Executable = @"C:\payload\a.exe");
            });
            p.Feature("FeatureB", f =>
            {
                f.Service(sharedPrefix + "BBB", s => s.Executable = @"C:\payload\b.exe");
            });
        });

        var result = new ComponentResolver(fs).Resolve(package);

        Assert.True(result.IsSuccess);
        var serviceComponentIds = result.Value.ServiceFeatureComponents.Values.ToList();
        Assert.Equal(2, serviceComponentIds.Count);
        Assert.Equal(serviceComponentIds.Distinct().Count(), serviceComponentIds.Count);
    }
}
