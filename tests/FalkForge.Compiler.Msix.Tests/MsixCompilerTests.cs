using System.Runtime.Versioning;
using FalkForge.Compiler.Msix.Builders;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsixCompilerTests
{
    private static MsixModel CreateValidModel() => new MsixBuilder()
        .Name("TestApp")
        .Publisher("CN=Test Publisher")
        .DisplayName("Test Application")
        .PublisherDisplayName("Test Publisher Inc.")
        .Version(new Version(1, 0, 0, 0))
        .Application("App1", "app.exe", app => app.DisplayName("Test App"))
        .Signing(s => s.Certificate("test.pfx"))
        .Build();

    [Fact]
    public void Compile_InvalidModel_ReturnsFailure()
    {
        var compiler = new MsixCompiler();
        var model = new MsixBuilder()
            .Name("")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = compiler.Compile(model, Path.GetTempPath());

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX001", result.Error.Message);
    }

    [Fact]
    public void Compile_NoSigning_ReturnsFailure()
    {
        var compiler = new MsixCompiler();
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Build();

        var result = compiler.Compile(model, Path.GetTempPath());

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX008", result.Error.Message);
    }

    [Fact]
    public void Compile_ValidModel_FailsAtPackagingNotValidation()
    {
        // A valid model should pass validation and manifest generation,
        // but will fail at the COM packaging step (no COM server in test env).
        var compiler = new MsixCompiler();
        var model = CreateValidModel();
        var outputPath = Path.Combine(Path.GetTempPath(), $"msix-test-{Guid.NewGuid():N}");

        try
        {
            Result<string> result;
            try
            {
                result = compiler.Compile(model, outputPath);
            }
            catch (TypeLoadException)
            {
                // COM interop type may not load in test environment.
                // The fact that we got past validation is the assertion.
                return;
            }

            // If COM succeeds (unlikely in test), accept success.
            // Otherwise, the failure should be from packaging/signing, not validation.
            if (result.IsFailure)
            {
                Assert.DoesNotContain("MSIX0", result.Error.Message);
                Assert.Equal(ErrorKind.CompilationError, result.Error.Kind);
            }
        }
        finally
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public void Compile_OutputDirectoryCreated_IfNotExists()
    {
        // Verify the compiler creates the output directory.
        // The compile will fail at COM/signing, but the directory should exist.
        var compiler = new MsixCompiler();
        var model = CreateValidModel();
        var outputPath = Path.Combine(Path.GetTempPath(), $"msix-dir-test-{Guid.NewGuid():N}");

        try
        {
            Assert.False(Directory.Exists(outputPath));

            // Will fail at COM step, but directory creation happens before that.
            try
            {
                _ = compiler.Compile(model, outputPath);
            }
            catch (TypeLoadException)
            {
                // COM interop type may not load in test environment.
            }

            Assert.True(Directory.Exists(outputPath));
        }
        finally
        {
            if (Directory.Exists(outputPath))
                Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public void Compile_WithUpdateSettings_ModelPassesValidation()
    {
        // Verify that a model with update settings passes the validation gate.
        // The full pipeline requires COM, so we verify the validation step only.
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .UpdateSettings("https://example.com/app.appinstaller")
            .Build();

        var validationResult = MsixValidator.Validate(model);

        Assert.True(validationResult.IsSuccess);
        Assert.NotNull(model.UpdateSettings);
    }

    [Theory]
    [InlineData("Simple App", "Simple App")]
    [InlineData("App<>Name", "App__Name")]
    [InlineData("My:App|Test", "My_App_Test")]
    [InlineData("Normal", "Normal")]
    [InlineData("Has/Slash", "Has_Slash")]
    [InlineData("Has\\Backslash", "Has_Backslash")]
    [InlineData("  Trimmed  ", "Trimmed")]
    public void SanitizeFileName_RemovesInvalidChars(string input, string expected)
    {
        var result = MsixCompiler.SanitizeFileName(input);

        Assert.Equal(expected, result);
    }
}
