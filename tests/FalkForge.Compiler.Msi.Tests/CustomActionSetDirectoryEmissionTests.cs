using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class CustomActionSetDirectoryEmissionTests
{
    [Fact]
    public void SetDirectory_EmitsType35CustomActionWithDirectorySourceAndFormattedTarget()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"CaSetDirTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for custom action set-directory test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "SetDirApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "SetDirApp"));
                p.CustomAction("CA_SetInstallDir", ca =>
                {
                    ca.SetDirectory("CUSTOM_TARGET_DIR", "[EXISTING_INSTALL_PATH]");
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;
            var rows = db.QueryRows(
                "SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`", 4);
            Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

            var row = Assert.Single(rows.Value);
            Assert.Equal("CA_SetInstallDir", row[0]);
            Assert.Equal("35", row[1]);
            Assert.Equal("CUSTOM_TARGET_DIR", row[2]);
            Assert.Equal("[EXISTING_INSTALL_PATH]", row[3]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
