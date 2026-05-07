using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Verifies that <see cref="MsiCompiler"/> invokes registered
/// <see cref="IExtensionValidator"/> instances before emitting tables, and that
/// any validation error returned by an extension aborts compilation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExtensionValidatorIntegrationTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static (PackageModel Package, string OutputDir, string TempDir) CreateMinimalPackage()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ExtValTest_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "app.exe"), "fake content");

        string outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        PackageModel package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "ExtValApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(Path.Combine(sourceDir, "app.exe"))
                          .To(KnownFolder.ProgramFiles / "TestCorp" / "ExtValApp"));
        });

        return (package, outputDir, tempDir);
    }

    // ---------------------------------------------------------------------------
    // Stub validators
    // ---------------------------------------------------------------------------

    /// <summary>Validator that always adds a single error.</summary>
    private sealed class AlwaysFailValidator : IExtensionValidator
    {
        public const string ErrorCode = "EXT001";
        public const string ErrorMessage = "Extension validation intentionally failed.";

        public int CallCount { get; private set; }

        public void Validate(ExtensionContext context, ValidationResult result)
        {
            CallCount++;
            result.AddError(ErrorCode, ErrorMessage);
        }
    }

    /// <summary>Validator that records calls but never adds errors.</summary>
    private sealed class AlwaysPassValidator : IExtensionValidator
    {
        public int CallCount { get; private set; }

        public void Validate(ExtensionContext context, ValidationResult result)
        {
            CallCount++;
        }
    }

    /// <summary>
    /// Extension that registers exactly one validator with the registry.
    /// </summary>
    private sealed class SingleValidatorExtension(IExtensionValidator validator, string name = "SingleValidatorExtension") : IFalkForgeExtension
    {
        public string Name => name;

        public void Register(IExtensionRegistry registry)
        {
            registry.RegisterValidator(validator);
        }
    }

    // ---------------------------------------------------------------------------
    // Tests — skipped until validator wiring is implemented in commit 2
    // ---------------------------------------------------------------------------

    [Fact]
    public void Compile_WithFailingExtensionValidator_ReturnsFailure()
    {
        // Arrange
        var (package, outputDir, tempDir) = CreateMinimalPackage();
        try
        {
            var failingValidator = new AlwaysFailValidator();
            var compiler = new MsiCompiler(new WindowsFileSystem(), [new SingleValidatorExtension(failingValidator)]);

            // Act
            Result<string> result = compiler.Compile(package, outputDir);

            // Assert — compilation must fail
            Assert.True(result.IsFailure,
                "Expected compilation to fail when an extension validator returns an error.");
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
            Assert.Contains(AlwaysFailValidator.ErrorCode, result.Error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_WithPassingExtensionValidator_Succeeds()
    {
        // Arrange
        var (package, outputDir, tempDir) = CreateMinimalPackage();
        try
        {
            var passingValidator = new AlwaysPassValidator();
            var compiler = new MsiCompiler(new WindowsFileSystem(), [new SingleValidatorExtension(passingValidator)]);

            // Act
            Result<string> result = compiler.Compile(package, outputDir);

            // Assert — validator was called and compilation succeeded
            Assert.Equal(1, passingValidator.CallCount);
            Assert.True(result.IsSuccess,
                $"Compilation with a passing validator should succeed. Error: {(result.IsFailure ? result.Error.Message : "")}");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_WithMultipleExtensionsAnyFailing_AggregatesErrors()
    {
        // Arrange: two extensions — one failing, one passing.
        // Both validators must be called; the combined error must name at least
        // the failing extension's error code.
        var (package, outputDir, tempDir) = CreateMinimalPackage();
        try
        {
            var failingValidator = new AlwaysFailValidator();
            var passingValidator = new AlwaysPassValidator();

            var compiler = new MsiCompiler(
                new WindowsFileSystem(),
                [
                    new SingleValidatorExtension(failingValidator, "FailingExtension"),
                    new SingleValidatorExtension(passingValidator, "PassingExtension")
                ]);

            // Act
            Result<string> result = compiler.Compile(package, outputDir);

            // Assert
            Assert.True(result.IsFailure,
                "Expected failure when at least one extension validator returns an error.");
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
            Assert.Contains(AlwaysFailValidator.ErrorCode, result.Error.Message,
                StringComparison.Ordinal);

            // Both validators must have been called (aggregate, not short-circuit).
            Assert.Equal(1, failingValidator.CallCount);
            Assert.Equal(1, passingValidator.CallCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ValidatorCalledBeforeAnyDatabaseIO()
    {
        // Arrange: the failing validator records the call. The assertion proves
        // validators run before any MSI file is created (the output directory is
        // empty after a failed compile driven by a validator error).
        var (package, outputDir, tempDir) = CreateMinimalPackage();
        try
        {
            var failingValidator = new AlwaysFailValidator();
            var compiler = new MsiCompiler(new WindowsFileSystem(), [new SingleValidatorExtension(failingValidator)]);

            // Act
            Result<string> result = compiler.Compile(package, outputDir);

            // Assert — compilation failed and no .msi was left on disk
            Assert.True(result.IsFailure);
            Assert.Equal(1, failingValidator.CallCount);

            var msiFiles = Directory.GetFiles(outputDir, "*.msi");
            Assert.Empty(msiFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
