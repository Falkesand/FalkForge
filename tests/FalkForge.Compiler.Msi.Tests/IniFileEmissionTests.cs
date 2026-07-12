using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class IniFileEmissionTests
{
    [Fact]
    public void Directory_SetsDirPropertyColumnOnCompiledMsi()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"IniFileDirTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for ini file directory test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "IniDirApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "IniDirApp"));
                p.IniFile("settings.ini", i => i
                    .Directory("CUSTOM_INI_DIR")
                    .Section("Database")
                    .Key("ConnectionString")
                    .Value("Server=local"));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;
            var rows = db.QueryRows(
                "SELECT `IniFile`, `DirProperty`, `Section` FROM `IniFile`", 3);
            Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

            var row = Assert.Single(rows.Value);
            Assert.Equal("CUSTOM_INI_DIR", row[1]);
            Assert.Equal("Database", row[2]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
