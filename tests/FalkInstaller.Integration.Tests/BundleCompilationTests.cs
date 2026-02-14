using System.Security.Cryptography;
using FalkInstaller.Compiler.Bundle;
using FalkInstaller.Compiler.Bundle.Builders;
using FalkInstaller.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkInstaller.Integration.Tests;

public sealed class BundleCompilationTests
{
    [Fact]
    public void Compile_SingleMsiPackage_ProducesValidBundle()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Create a fake MSI file with known content
            var msiContent = RandomNumberGenerator.GetBytes(256);
            var msiPath = Path.Combine(tempDir, "TestApp.msi");
            File.WriteAllBytes(msiPath, msiContent);

            var model = new BundleBuilder()
                .Name("TestApp")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Chain(chain => chain
                    .MsiPackage(msiPath, pkg => pkg
                        .Id("TestMsi")
                        .DisplayName("Test MSI Package")
                        .Version("1.0.0")))
                .Build();

            var compiler = new BundleCompiler();
            var outputDir = Path.Combine(tempDir, "output");
            var result = compiler.Compile(model, outputDir);

            Assert.True(result.IsSuccess, $"Compilation failed: {(result.IsFailure ? result.Error.Message : "")}");

            var outputExe = result.Value;
            Assert.True(File.Exists(outputExe), $"Output EXE not found at: {outputExe}");
            Assert.True(new FileInfo(outputExe).Length > 0, "Output EXE is empty");

            // Extract and verify TOC
            var extractResult = PayloadEmbedder.Extract(outputExe);
            Assert.True(extractResult.IsSuccess, $"Extraction failed: {(extractResult.IsFailure ? extractResult.Error.Message : "")}");

            var content = extractResult.Value;
            Assert.Single(content.TocEntries);
            Assert.Equal("TestMsi", content.TocEntries[0].PackageId);
            Assert.Equal(msiContent.Length, content.TocEntries[0].OriginalSize);

            var expectedHash = Convert.ToHexString(SHA256.HashData(msiContent));
            Assert.Equal(expectedHash, content.TocEntries[0].Sha256Hash);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Compile_MultiplePackages_AllPresentInToc()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Create multiple fake package files
            var msi1Content = RandomNumberGenerator.GetBytes(128);
            var msi1Path = Path.Combine(tempDir, "AppCore.msi");
            File.WriteAllBytes(msi1Path, msi1Content);

            var msi2Content = RandomNumberGenerator.GetBytes(512);
            var msi2Path = Path.Combine(tempDir, "AppPlugins.msi");
            File.WriteAllBytes(msi2Path, msi2Content);

            var exeContent = RandomNumberGenerator.GetBytes(64);
            var exePath = Path.Combine(tempDir, "Prerequisites.exe");
            File.WriteAllBytes(exePath, exeContent);

            var model = new BundleBuilder()
                .Name("MultiPkgApp")
                .Manufacturer("Integration Tests")
                .Version("2.5.0")
                .UseSilentUI()
                .Chain(chain => chain
                    .MsiPackage(msi1Path, pkg => pkg
                        .Id("AppCore")
                        .DisplayName("Application Core"))
                    .MsiPackage(msi2Path, pkg => pkg
                        .Id("AppPlugins")
                        .DisplayName("Application Plugins"))
                    .ExePackage(exePath, pkg => pkg
                        .Id("Prerequisites")
                        .DisplayName("Prerequisites Package")))
                .Build();

            var compiler = new BundleCompiler();
            var outputDir = Path.Combine(tempDir, "output");
            var result = compiler.Compile(model, outputDir);

            Assert.True(result.IsSuccess, $"Compilation failed: {(result.IsFailure ? result.Error.Message : "")}");

            var extractResult = PayloadEmbedder.Extract(result.Value);
            Assert.True(extractResult.IsSuccess);

            var content = extractResult.Value;
            Assert.Equal(3, content.TocEntries.Length);

            // Verify each package is in the TOC in order
            Assert.Equal("AppCore", content.TocEntries[0].PackageId);
            Assert.Equal(msi1Content.Length, content.TocEntries[0].OriginalSize);

            Assert.Equal("AppPlugins", content.TocEntries[1].PackageId);
            Assert.Equal(msi2Content.Length, content.TocEntries[1].OriginalSize);

            Assert.Equal("Prerequisites", content.TocEntries[2].PackageId);
            Assert.Equal(exeContent.Length, content.TocEntries[2].OriginalSize);

            // Verify hashes match source files
            Assert.Equal(Convert.ToHexString(SHA256.HashData(msi1Content)), content.TocEntries[0].Sha256Hash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(msi2Content)), content.TocEntries[1].Sha256Hash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(exeContent)), content.TocEntries[2].Sha256Hash);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Compile_InvalidModel_ReturnsFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Model with no name -- validation should fail
            var model = new BundleBuilder()
                .Name("")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .Build();

            var compiler = new BundleCompiler();
            var result = compiler.Compile(model, tempDir);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Compile_MissingSourceFile_ReturnsFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var model = new BundleBuilder()
                .Name("TestApp")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .Chain(chain => chain
                    .MsiPackage(Path.Combine(tempDir, "nonexistent.msi"), pkg => pkg
                        .Id("Missing")
                        .DisplayName("Missing Package")))
                .Build();

            var compiler = new BundleCompiler();
            var outputDir = Path.Combine(tempDir, "output");
            var result = compiler.Compile(model, outputDir);

            Assert.True(result.IsFailure);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Compile_ThenExtract_CompressedSizeIsSmaller()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Create a compressible file (repeated pattern compresses well)
            var msiContent = new byte[4096];
            for (var i = 0; i < msiContent.Length; i++)
                msiContent[i] = (byte)(i % 16);

            var msiPath = Path.Combine(tempDir, "Compressible.msi");
            File.WriteAllBytes(msiPath, msiContent);

            var model = new BundleBuilder()
                .Name("CompressTest")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Chain(chain => chain
                    .MsiPackage(msiPath, pkg => pkg
                        .Id("CompressiblePkg")
                        .DisplayName("Compressible Package")))
                .Build();

            var compiler = new BundleCompiler();
            var outputDir = Path.Combine(tempDir, "output");
            var result = compiler.Compile(model, outputDir);
            Assert.True(result.IsSuccess);

            var extractResult = PayloadEmbedder.Extract(result.Value);
            Assert.True(extractResult.IsSuccess);

            var entry = extractResult.Value.TocEntries[0];
            Assert.Equal(msiContent.Length, entry.OriginalSize);
            Assert.True(entry.CompressedSize < entry.OriginalSize,
                $"Expected compressed size ({entry.CompressedSize}) to be less than original ({entry.OriginalSize})");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
    }
}
