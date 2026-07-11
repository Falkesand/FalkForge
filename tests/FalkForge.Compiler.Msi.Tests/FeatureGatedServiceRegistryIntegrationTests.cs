using System.Runtime.Versioning;
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
}
