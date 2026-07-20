using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Msi;
using FalkForge.Testing;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Command-level wiring coverage for <see cref="WinGetCommand"/>. The underlying
/// <c>WinGetManifestGenerator</c> / <c>WinGetManifestWriter</c> are already unit-tested elsewhere;
/// what was untested is the COMMAND: that it binds its settings, inspects the MSI, resolves the
/// output directory, produces the manifest files there, and returns the right exit codes for the
/// happy path and for a missing input. These tests exercise the command directly against a test
/// console, mirroring <see cref="InspectCommandTests"/> and <see cref="ExtractCommandTests"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinGetCommandTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"WinGetCmdTest_{Guid.NewGuid():N}");

    public WinGetCommandTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "winget", null);

    [Fact]
    public void Execute_ValidMsi_WritesManifestFilesAndReturnsSuccess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "winget command requires Windows (msi.dll)");

        var msiPath = CompileMsi("WinGetHappy");
        var outputDir = Path.Combine(_tempDir, "manifests");

        var console = new TestConsoleOutput();
        var command = new WinGetCommand(console);
        var settings = new WinGetSettings
        {
            MsiPath = msiPath,
            PackageIdentifier = "Contoso.WinGetHappy",
            License = "MIT",
            ShortDescription = "A test package",
            InstallerUrl = "https://example.com/WinGetHappy-1.0.0.msi",
            OutputDir = outputDir
        };

        var exit = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exit);

        // The 3-file WinGet manifest set must have been produced under the output directory.
        var yaml = Directory.GetFiles(outputDir, "*.yaml", SearchOption.AllDirectories);
        Assert.Contains(yaml, f => f.EndsWith("Contoso.WinGetHappy.yaml", StringComparison.Ordinal));
        Assert.Contains(yaml, f => f.EndsWith("Contoso.WinGetHappy.installer.yaml", StringComparison.Ordinal));
        Assert.Contains(yaml, f => f.EndsWith("Contoso.WinGetHappy.locale.en-US.yaml", StringComparison.Ordinal));

        // The command reports where it wrote them.
        Assert.Contains(console.MarkupLines, m => m.Contains("WinGet manifests written to", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_MissingMsi_ReturnsRuntimeErrorAndWritesError()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "winget command requires Windows (msi.dll)");

        var missing = Path.Combine(_tempDir, "does_not_exist.msi");

        var console = new TestConsoleOutput();
        var command = new WinGetCommand(console);
        var settings = new WinGetSettings
        {
            MsiPath = missing,
            PackageIdentifier = "Contoso.Missing",
            License = "MIT",
            ShortDescription = "A test package",
            InstallerUrl = "https://example.com/Missing-1.0.0.msi",
            OutputDir = Path.Combine(_tempDir, "out")
        };

        var exit = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);

        Assert.NotEqual(ExitCodes.Success, exit);
        Assert.Equal(ExitCodes.RuntimeError, exit);
        Assert.Contains(console.Errors, e => e.Contains("File not found", StringComparison.Ordinal));
    }

    // --- Settings validation (platform-independent; no MSI / msi.dll required) ---

    [Fact]
    public void Validate_AllRequiredFieldsPresent_Succeeds()
    {
        var settings = new WinGetSettings
        {
            MsiPath = "app.msi",
            PackageIdentifier = "Contoso.App",
            License = "MIT",
            ShortDescription = "desc"
        };

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_MissingId_ReturnsError()
    {
        var settings = new WinGetSettings
        {
            MsiPath = "app.msi",
            License = "MIT",
            ShortDescription = "desc"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--id", result.Message);
    }

    [Fact]
    public void Validate_IdWithoutPublisherDotName_ReturnsError()
    {
        var settings = new WinGetSettings
        {
            MsiPath = "app.msi",
            PackageIdentifier = "NoDotHere",
            License = "MIT",
            ShortDescription = "desc"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("Publisher.PackageName", result.Message);
    }

    [Fact]
    public void Validate_NonMsiExtension_ReturnsError()
    {
        var settings = new WinGetSettings
        {
            MsiPath = "app.exe",
            PackageIdentifier = "Contoso.App",
            License = "MIT",
            ShortDescription = "desc"
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains(".msi", result.Message);
    }

    private string CompileMsi(string label)
    {
        var sourceDir = Path.Combine(_tempDir, $"{label}_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"content for {label}");

        var outputDir = Path.Combine(_tempDir, $"{label}_output");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = label;
            p.Manufacturer = "Contoso";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Contoso" / label));
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }
}
