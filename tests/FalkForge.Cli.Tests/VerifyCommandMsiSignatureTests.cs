using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Msi;
using FalkForge.Testing;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for <see cref="VerifyCommand"/>'s signature-only path: <c>forge verify &lt;msi&gt;</c>
/// with no <c>--rebuild</c>. Exercises the real <see cref="MsiIntegrityVerifier"/> against a real
/// compiled MSI (Windows-only, requires msi.dll) rather than a fake, since the whole point of this
/// mode is checking an actual cryptographic signature.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VerifyCommandMsiSignatureTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"VerifyCmdMsiSig_{Guid.NewGuid():N}");

    public VerifyCommandMsiSignatureTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private static CommandContext Ctx() =>
        new([], new EmptyRemainingArguments(), "verify", null);

    private string CompileMsi(string label, bool signed)
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
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / label));
            if (signed)
                p.Integrity(i => { });
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public void Execute_SignedMsi_NoRebuild_ReturnsSuccessWithVerifiedVerdict()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var msiPath = CompileMsi(nameof(Execute_SignedMsi_NoRebuild_ReturnsSuccessWithVerifiedVerdict), signed: true);
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output);
        var settings = new VerifySettings { ArtifactPath = msiPath };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains(output.AllOutput, m => m.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_UnsignedMsi_NoRebuild_ReturnsValidationFailureWithNotSignedVerdict()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var msiPath = CompileMsi(nameof(Execute_UnsignedMsi_NoRebuild_ReturnsValidationFailureWithNotSignedVerdict), signed: false);
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output);
        var settings = new VerifySettings { ArtifactPath = msiPath };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.ValidationFailure, code);
        Assert.Contains(output.AllOutput, m => m.Contains("NOT-SIGNED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_SignedMsi_WrongTrustedKey_ReturnsValidationFailure()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var msiPath = CompileMsi(nameof(Execute_SignedMsi_WrongTrustedKey_ReturnsValidationFailure), signed: true);
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output);
        var settings = new VerifySettings
        {
            ArtifactPath = msiPath,
            TrustedKeys = ["0000000000000000000000000000000000000000000000000000000000000000"]
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.ValidationFailure, code);
        Assert.Contains(output.AllOutput, m => m.Contains("FAILED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_JsonMode_MsiSignatureOnly_EmitsVerdictInEnvelope()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var msiPath = CompileMsi(nameof(Execute_JsonMode_MsiSignatureOnly_EmitsVerdictInEnvelope), signed: true);
        var sink = new StringWriter();
        var command = new VerifyCommand(new TestConsoleOutput(), jsonSink: sink);
        var settings = new VerifySettings { ArtifactPath = msiPath, Json = true };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        var json = sink.ToString();
        Assert.Contains("\"command\":\"verify\"", json);
        Assert.Contains("VERIFIED", json);
    }

    [Fact]
    public void Execute_ExeArtifact_NoRebuild_ReturnsRuntimeError()
    {
        // .exe has no signature-only mode; VerifySettings.Validate() rejects this combination at
        // the CLI-parsing layer, but Execute() must not assume Validate() already ran (existing
        // tests call ExecuteSync directly, bypassing Spectre's validation pipeline) — so it needs
        // its own defensive check.
        var artifact = Path.Combine(_tempDir, "bundle.exe");
        File.WriteAllBytes(artifact, [1, 2, 3]);
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output);
        var settings = new VerifySettings { ArtifactPath = artifact };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, code);
    }
}
