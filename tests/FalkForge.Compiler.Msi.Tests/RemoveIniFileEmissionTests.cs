using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class RemoveIniFileEmissionTests
{
    [Fact]
    public void RemoveIniFile_EmitsRowInCompiledMsi()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"RemIniTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for remove ini file test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "RemIniApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "RemIniApp"));
                p.RemoveIniFile("settings.ini", r => r
                    .Id("RemIni1")
                    .Directory("INSTALLDIR")
                    .Section("Database")
                    .Key("ConnectionString")
                    .Action(IniFileAction.RemoveTag));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;
            var rows = db.QueryRows(
                "SELECT `RemoveIniFile`, `FileName`, `DirProperty`, `Section`, `Key`, `Action` FROM `RemoveIniFile`", 6);
            Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

            var row = Assert.Single(rows.Value);
            Assert.Equal("RemIni1", row[0]);
            Assert.Equal("settings.ini", row[1]);
            Assert.Equal("INSTALLDIR", row[2]);
            Assert.Equal("Database", row[3]);
            Assert.Equal("ConnectionString", row[4]);
            Assert.Equal(((int)IniFileAction.RemoveTag).ToString(), row[5]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
