using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Integrity;
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

    /// <summary>
    /// Mirrors <c>MsiIntegrityVerifierTests.ReadFirstFingerprint</c>: reads the CLASSICAL
    /// (ECDSA-P256) signature's fingerprint, since <c>--trusted-key</c> only ever matches against
    /// that entry (a hybrid-signed zero-config build also carries an ML-DSA companion entry).
    /// </summary>
    private static string ReadClassicalFingerprint(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        Assert.True(dbResult.IsSuccess);
        using var db = dbResult.Value;
        var rows = db.QueryRows("SELECT `Id`, `Data` FROM `_FalkForgeIntegrity`", 2);
        Assert.True(rows.IsSuccess);
        var manifestRow = Assert.Single(rows.Value, r => r[0] == "ManifestSignature");
        var envelope = IntegrityEnvelopeCodec.Parse(manifestRow[1]!);
        Assert.NotNull(envelope);
        var classical = envelope!.Signatures.First(s => string.IsNullOrEmpty(s.Algorithm));
        return classical.Fingerprint;
    }

    [Fact]
    public void Execute_SignedMsi_NoRebuild_NoTrustedKey_ReturnsSuccessWithConsistencyOnlyLabel()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Merge Gate MEDIUM finding (Opus): consistency-only verification (no --trusted-key) must
        // NOT print an identical "VERIFIED" as authorship-established verification — that is a
        // downgrade-attack UX (a user could be shown the same green PASS regardless of whether
        // publisher identity was actually checked). Exit code stays 0 (the payload genuinely is
        // self-consistent), but the label must say so plainly.
        var msiPath = CompileMsi(nameof(Execute_SignedMsi_NoRebuild_NoTrustedKey_ReturnsSuccessWithConsistencyOnlyLabel), signed: true);
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output);
        var settings = new VerifySettings { ArtifactPath = msiPath };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains(output.AllOutput, m => m.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(output.AllOutput, m =>
            m.Contains("tamper-evidence only", StringComparison.OrdinalIgnoreCase)
            && m.Contains("authorship NOT established", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_SignedMsi_NoRebuild_WithCorrectTrustedKey_ReturnsSuccessWithAuthorshipLabel_DistinctFromConsistencyOnly()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // A short literal label, not nameof(this test): the full test method name is long enough
        // that combining it with the temp-dir prefix and "_output\<label>-1.0.0.msi" pushes the
        // compiled MSI's path past MAX_PATH (260 chars), which MsiDatabase.Create then reports as
        // the misleadingly generic "Error code: 1631" rather than a path-length error.
        var msiPath = CompileMsi("AuthorshipLabelApp", signed: true);
        var fingerprint = ReadClassicalFingerprint(msiPath);

        var pinnedOutput = new TestConsoleOutput();
        var pinnedCode = new VerifyCommand(pinnedOutput).ExecuteSync(
            Ctx(), new VerifySettings { ArtifactPath = msiPath, TrustedKeys = [fingerprint] }, CancellationToken.None);

        var consistencyOnlyOutput = new TestConsoleOutput();
        var consistencyOnlyCode = new VerifyCommand(consistencyOnlyOutput).ExecuteSync(
            Ctx(), new VerifySettings { ArtifactPath = msiPath }, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, pinnedCode);
        Assert.Equal(ExitCodes.Success, consistencyOnlyCode);

        // Both PASS (exit 0), but the labels must differ — a pinned, authorship-verified PASS is a
        // strictly stronger claim than a consistency-only PASS, and the two must never render
        // identically (the exact downgrade-attack UX the Merge Gate flagged).
        Assert.Contains(pinnedOutput.AllOutput, m => m.Contains("authorship verified", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(pinnedOutput.AllOutput, m => m.Contains("tamper-evidence only", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(consistencyOnlyOutput.AllOutput, m => m.Contains("tamper-evidence only", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(consistencyOnlyOutput.AllOutput, m => m.Contains("authorship verified", StringComparison.OrdinalIgnoreCase));
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
