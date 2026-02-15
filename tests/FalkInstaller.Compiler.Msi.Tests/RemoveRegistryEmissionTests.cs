using System.Runtime.Versioning;
using FalkInstaller.Models;
using FalkInstaller.Platform.Windows;
using FalkInstaller.Testing;
using Xunit;

namespace FalkInstaller.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class RemoveRegistryEmissionTests
{
    [Fact]
    public void RemoveKeyAction_EmitsNullName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"RemRegTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for remove registry test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "RemRegApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "RemRegApp"));
                p.RemoveRegistry(r => r
                    .Id("RemKey1")
                    .Root(RegistryRoot.LocalMachine)
                    .Key(@"SOFTWARE\OldApp")
                    .RemoveKey());
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;
            var rows = db.QueryRows(
                "SELECT `RemoveRegistry`, `Root`, `Key`, `Name`, `Component_` FROM `RemoveRegistry`", 5);
            Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

            var row = Assert.Single(rows.Value);
            Assert.Equal("RemKey1", row[0]);
            Assert.Null(row[3]); // Name must be null for RemoveKey action
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RemoveValueAction_EmitsSpecifiedName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"RemValTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for remove value test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "RemValApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "RemValApp"));
                p.RemoveRegistry(r => r
                    .Id("RemVal1")
                    .Root(RegistryRoot.CurrentUser)
                    .Key(@"SOFTWARE\MyApp\Settings")
                    .Name("OldSetting")
                    .RemoveValue());
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;
            var rows = db.QueryRows(
                "SELECT `RemoveRegistry`, `Root`, `Key`, `Name`, `Component_` FROM `RemoveRegistry`", 5);
            Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

            var row = Assert.Single(rows.Value);
            Assert.Equal("RemVal1", row[0]);
            Assert.Equal("OldSetting", row[3]); // Name must be the specified value for RemoveValue action
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RemoveKeyAction_WithNameSet_StillEmitsNullName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"RemKeyNameTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for remove key with name test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "RemKeyNameApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "RemKeyNameApp"));
                p.RemoveRegistry(r => r
                    .Id("RemKey2")
                    .Root(RegistryRoot.LocalMachine)
                    .Key(@"SOFTWARE\OldApp")
                    .Name("ShouldBeIgnored")
                    .RemoveKey());
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;
            var rows = db.QueryRows(
                "SELECT `RemoveRegistry`, `Root`, `Key`, `Name`, `Component_` FROM `RemoveRegistry`", 5);
            Assert.True(rows.IsSuccess, $"Query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

            var row = Assert.Single(rows.Value);
            Assert.Equal("RemKey2", row[0]);
            Assert.Null(row[3]); // Name must be null for RemoveKey, even if model has a Name set
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
