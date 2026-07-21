namespace FalkForge.Engine.Tests.Msi;

using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Proves <see cref="MsiFeatureReader.Read"/> surfaces a compiled MSI's <c>Feature</c>
/// table — feature ids, titles/descriptions, and the parent foreign key — so the UI can
/// build a per-package feature picker. Windows-only: compiling and reopening a real MSI
/// needs msi.dll, mirroring the other compiled-MSI integration tests.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiFeatureReaderTests
{
    [Fact]
    public void Read_CompiledMsiWithParentChildFeatures_ReturnsFeaturesWithParentTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeatReader_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var coreFile = Path.Combine(tempDir, "core.exe");
            File.WriteAllText(coreFile, "core payload");
            var docsFile = Path.Combine(tempDir, "docs.txt");
            File.WriteAllText(docsFile, "docs payload");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FeatureReaderApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("Core", core =>
                {
                    core.Title = "Core Components";
                    core.Description = "Required runtime files";
                    core.Files(fs => fs.Add(coreFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatureReaderApp"));

                    core.Feature("Docs", docs =>
                    {
                        docs.Title = "Documentation";
                        // Description intentionally left null to exercise the null-column path.
                        docs.Files(fs => fs.Add(docsFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatureReaderApp" / "docs"));
                    });
                });
            });

            var compileResult = new MsiCompiler(new WindowsFileSystem()).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                compileResult.IsFailure ? compileResult.Error.Message : null);

            var readResult = MsiFeatureReader.Read(compileResult.Value);

            Assert.True(readResult.IsSuccess, readResult.IsFailure ? readResult.Error.Message : null);
            var features = readResult.Value;

            var core = features.Single(f => f.FeatureId == "Core");
            var docs = features.Single(f => f.FeatureId == "Docs");

            // Parent tree: Core is a root feature, Docs hangs off Core.
            Assert.Null(core.Parent);
            Assert.Equal("Core", docs.Parent);

            // Titles/descriptions round-trip, including the null-description column.
            Assert.Equal("Core Components", core.Title);
            Assert.Equal("Required runtime files", core.Description);
            Assert.Equal("Documentation", docs.Title);
            Assert.Null(docs.Description);

            // Default features install at level 1; EstimatedSize is deferred (0 for now).
            Assert.Equal(1, core.Level);
            Assert.Equal(0, core.EstimatedSize);
            Assert.Equal(0, docs.EstimatedSize);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
