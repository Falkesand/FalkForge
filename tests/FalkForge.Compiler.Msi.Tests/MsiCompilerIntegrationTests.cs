using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsiCompilerIntegrationTests
{
    [Fact]
    public void Compile_ValidPackageWithFiles_CreatesMsiFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake executable content for testing");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "IntegrationTestApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrationTestApp"));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
            Assert.True(File.Exists(result.Value), $"MSI file not found at: {result.Value}");
            Assert.True(new FileInfo(result.Value).Length > 0, "MSI file is empty");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ValidPackageWithFiles_MsiHasExpectedTables()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake executable content for table test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "TableTestApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(2, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "TableTestApp"));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            // Open the MSI and verify expected tables exist by querying them
            var dbResult = MsiDatabase.Open(compileResult.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;

            // These core tables should exist in any valid MSI with files
            AssertTableExists(db, "Property");
            AssertTableExists(db, "File");
            AssertTableExists(db, "Component");
            AssertTableExists(db, "Directory");
            AssertTableExists(db, "Feature");
            AssertTableExists(db, "FeatureComponents");
            AssertTableExists(db, "Media");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_InvalidPackage_ReturnsValidationError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Build a package with invalid data (empty name/manufacturer)
            var package = new PackageModel
            {
                Name = "",
                Manufacturer = "",
                Version = new Version(1, 0, 0),
                UpgradeCode = Guid.NewGuid(),
                ProductCode = Guid.NewGuid(),
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

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var result = compiler.Compile(package, tempDir);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static void AssertTableExists(MsiDatabase db, string tableName)
    {
        var result = db.Execute($"SELECT * FROM `{tableName}`");
        Assert.True(result.IsSuccess, $"Table '{tableName}' not found in MSI database: {(result.IsFailure ? result.Error.Message : "")}");
    }
}
