using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class PowerShellCustomActionEmissionTests
{
    // Regression: PowerShellScript CA emitted Type=ExeInDir (Directory-source bit 0x20)
    // with Source="[SystemFolder]" (a formatted expression). MSI rejects this with
    // error 2727 because the Directory-source bit requires Source to be a Directory
    // table key AND that key must have a row in the Directory table. The fix emits
    // Source="SystemFolder" (key) and materializes a SystemFolder Directory row
    // under TARGETDIR so MSI can resolve it to the real system folder at install time.
    [Fact]
    public void Compile_PowerShellCustomAction_EmitsSystemFolderDirectoryRow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "payload");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "PsApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "PsApp"));
                p.CustomAction("CA_RunPs", ca =>
                {
                    ca.PowerShellScript("Write-Host 'hi'");
                });
            });

            var compileResult = new MsiCompiler(new WindowsFileSystem()).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            // CustomAction.Source must be the Directory key, not a formatted expression.
            var caRows = db.QueryRows("SELECT `Action`, `Source` FROM `CustomAction`", 2).Value;
            var caRow = Assert.Single(caRows, r => r[0] == "CA_RunPs");
            Assert.Equal("SystemFolder", caRow[1]);

            // A Directory row with primary key "SystemFolder" must exist, with parent TARGETDIR.
            var dirRows = db.QueryRows(
                "SELECT `Directory`, `Directory_Parent` FROM `Directory`", 2).Value;
            var sysRow = Assert.Single(dirRows, r => r[0] == "SystemFolder");
            Assert.Equal("TARGETDIR", sysRow[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
