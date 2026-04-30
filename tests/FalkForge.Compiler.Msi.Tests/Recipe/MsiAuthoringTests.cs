using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Phase 8 tests for the <see cref="MsiAuthoring.Compile"/> static facade.
/// The facade routes a <see cref="PackageModel"/> through the recipe-driven
/// pipeline (validate → resolve → recipe build → cabinet → executor → post)
/// and produces a real MSI on disk. These tests do not yet compare against
/// the legacy <see cref="MsiCompiler"/> output; phase 9 will add a
/// byte-diff harness.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiAuthoringTests : IDisposable
{
    private readonly string _tempDir;

    public MsiAuthoringTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MsiAuthoring_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; msi.dll occasionally retains a brief
                // handle on the file even after Dispose.
            }
        }
    }

    [Fact(Skip = "Round-trip requires DirectoryTableProducer synthesis (later phase). Re-enable once Directory tree synthesis lands.")]
    public void Compile_with_minimal_hello_world_produces_msi_at_output_path()
    {
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "hello.txt");
        File.WriteAllText(sourceFile, "hello world");

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "HelloWorld";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "HelloWorld"));
        });

        Result<string> result = MsiAuthoring.Compile(package, outputDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(File.Exists(result.Value), $"MSI file not produced at: {result.Value}");
        Assert.True(new FileInfo(result.Value).Length > 0, "MSI file is empty");
    }

    [Fact]
    public void Compile_with_invalid_package_returns_validation_failure()
    {
        // Build a PackageModel directly with an empty Name. PackageBuilder
        // would reject this at builder.Build(), so we construct the model
        // with a deliberately invalid Name to drive ModelValidator.
        PackageModel package = new()
        {
            Name = string.Empty,
            Manufacturer = "FalkForge",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
        };

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        Result<string> result = MsiAuthoring.Compile(package, outputDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact(Skip = "Round-trip requires DirectoryTableProducer synthesis (later phase). Re-enable once Directory tree synthesis lands.")]
    public void Compile_with_simple_package_round_trips_property_table()
    {
        string sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        string sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "fake exe content");

        string outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "RecipeRoundTrip";
            p.Manufacturer = "FalkForge";
            p.Version = new Version(2, 1, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "FalkForge" / "RoundTrip"));
        });

        Result<string> compileResult = MsiAuthoring.Compile(package, outputDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        using MsiDatabase db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
        Result<List<string?[]>> queryResult =
            db.QueryRows("SELECT `Property`, `Value` FROM `Property`", fieldCount: 2);
        Assert.True(queryResult.IsSuccess);

        List<string?[]> rows = queryResult.Value;
        Assert.Contains(rows, r => r[0] == "ProductName" && r[1] == "RecipeRoundTrip");
        Assert.Contains(rows, r => r[0] == "Manufacturer" && r[1] == "FalkForge");
    }
}
