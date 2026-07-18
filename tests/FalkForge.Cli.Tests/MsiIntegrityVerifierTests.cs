using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// End-to-end tests for <see cref="MsiIntegrityVerifier"/>: compiles a real MSI (Windows-only,
/// requires msi.dll) and verifies the pure-.NET ECDSA signature <c>IntegritySigner</c> embeds in
/// the <c>_FalkForgeIntegrity</c> table. Proves the whole chain — build-time signing, table/sidecar
/// storage, cabinet re-extraction, and hash recomputation — agrees with itself, and that tampering
/// (wrong trusted key, corrupted signature) is caught rather than silently passed.
///
/// <para>Class-level <see cref="SupportedOSPlatformAttribute"/> mirrors
/// <c>MsiIntegritySigningTests</c> (Compiler.Msi.Tests) — the established pattern for a test class
/// whose entire subject is msi.dll-backed. The per-test <c>Assert.Skip</c> runtime guards are extra
/// defense so the suite degrades gracefully rather than crashing if ever run on a non-Windows box.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiIntegrityVerifierTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"MsiVerifyTest_{Guid.NewGuid():N}");

    public MsiIntegrityVerifierTests()
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

    private (string sourceFile, string outputDir) CreatePackageInputs(string label, string content = "payload content")
    {
        var sourceDir = Path.Combine(_tempDir, $"{label}_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, content);

        var outputDir = Path.Combine(_tempDir, $"{label}_output");
        Directory.CreateDirectory(outputDir);

        return (sourceFile, outputDir);
    }

    private static string CompileSigned(string sourceFile, string outputDir, string label)
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = label;
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / label));
            p.Integrity(i => { });
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static string CompileUnsigned(string sourceFile, string outputDir, string label)
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = label;
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / label));
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public void Verify_SignedMsi_NoTrustedKeys_ReturnsVerified_ConsistencyOnly()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (source, outputDir) = CreatePackageInputs(nameof(Verify_SignedMsi_NoTrustedKeys_ReturnsVerified_ConsistencyOnly));
        var msiPath = CompileSigned(source, outputDir, "ConsistencyOnlyApp");

        var result = MsiIntegrityVerifier.Verify(msiPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignatureVerdict.Verified, result.Value.Verdict);
        Assert.Empty(result.Value.MismatchedFiles);
        Assert.Equal(IntegrityTableEmitterFormatTag, result.Value.FormatTag);
    }

    [Fact]
    public void Verify_SignedMsi_CorrectTrustedKey_ReturnsVerifiedWithMatchedFingerprint()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (source, outputDir) = CreatePackageInputs(nameof(Verify_SignedMsi_CorrectTrustedKey_ReturnsVerifiedWithMatchedFingerprint));
        var msiPath = CompileSigned(source, outputDir, "TrustedKeyApp");

        var actualFingerprint = ReadFirstFingerprint(msiPath);

        var result = MsiIntegrityVerifier.Verify(
            msiPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { actualFingerprint });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignatureVerdict.Verified, result.Value.Verdict);
        Assert.Equal(actualFingerprint, result.Value.MatchedFingerprint, ignoreCase: true);
    }

    [Fact]
    public void Verify_SignedMsi_WrongTrustedKey_ReturnsFailed()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (source, outputDir) = CreatePackageInputs(nameof(Verify_SignedMsi_WrongTrustedKey_ReturnsFailed));
        var msiPath = CompileSigned(source, outputDir, "WrongKeyApp");

        var bogusFingerprint = Convert.ToHexString(SHA256.HashData("not-the-real-key"u8.ToArray()));
        var result = MsiIntegrityVerifier.Verify(
            msiPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { bogusFingerprint });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignatureVerdict.Failed, result.Value.Verdict);
        Assert.Contains("INT001", result.Value.Message);
    }

    [Fact]
    public void Verify_UnsignedMsi_NoSidecar_ReturnsNotSigned()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (source, outputDir) = CreatePackageInputs(nameof(Verify_UnsignedMsi_NoSidecar_ReturnsNotSigned));
        var msiPath = CompileUnsigned(source, outputDir, "UnsignedApp");

        var result = MsiIntegrityVerifier.Verify(msiPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignatureVerdict.NotSigned, result.Value.Verdict);
    }

    [Fact]
    public void Verify_UnsignedMsi_WithDetachedSidecar_FallsBackToSidecarAndVerifies()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Proves the sidecar fallback independently of how the parent signing branch ends up
        // handling reproducible-mode MSIs: an MSI with NO _FalkForgeIntegrity table at all, but a
        // real matching .sig.json sitting beside it, must still verify via the sidecar.
        var (source, outputDir) = CreatePackageInputs(
            nameof(Verify_UnsignedMsi_WithDetachedSidecar_FallsBackToSidecarAndVerifies), "sidecar payload");
        var msiPath = CompileUnsigned(source, outputDir, "SidecarApp");

        var actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var entries = new List<PayloadHashEntry> { new("app.exe", actualSha256) };
        var signResult = EcdsaManifestSigner.Sign(entries, config: null);
        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : null);
        File.WriteAllText(msiPath + ".sig.json", signResult.Value);

        var result = MsiIntegrityVerifier.Verify(msiPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignatureVerdict.Verified, result.Value.Verdict);
        Assert.Contains("sidecar", result.Value.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_TamperedSignedEnvelope_ReturnsFailed()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (source, outputDir) = CreatePackageInputs(nameof(Verify_TamperedSignedEnvelope_ReturnsFailed));
        var msiPath = CompileSigned(source, outputDir, "TamperedApp");

        // Directly rewrite the embedded signature row's Data field via ordinary SQL UPDATE (no
        // special stream surgery needed — Data is a plain string column). This models an attacker
        // editing the declared hash without being able to re-sign: the cryptographic verification
        // below must catch it because the edited bytes no longer match the signature.
        var dbResult = MsiDatabase.Open(msiPath, readOnly: false);
        Assert.True(dbResult.IsSuccess, dbResult.IsFailure ? dbResult.Error.Message : null);
        using (var db = dbResult.Value)
        {
            var rows = db.QueryRows("SELECT `Id`, `Data` FROM `_FalkForgeIntegrity`", 2);
            Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : null);
            var manifestRow = Assert.Single(rows.Value, r => r[0] == "ManifestSignature");
            var tampered = manifestRow[1]!.Replace("\"sha256\"", "\"sha256x\"", StringComparison.Ordinal);
            // A trivial byte-level tamper of the signed JSON: flip content while keeping it valid
            // JSON is unnecessary — this exercises the "signature/content disagree" path either via
            // a parse failure or a verification failure, both of which must be a FAIL, never a PASS.
            var escaped = tampered.Replace("'", "''", StringComparison.Ordinal);
            var updateResult = db.Execute(
                $"UPDATE `_FalkForgeIntegrity` SET `Data` = '{escaped}' WHERE `Id` = 'ManifestSignature'");
            Assert.True(updateResult.IsSuccess, updateResult.IsFailure ? updateResult.Error.Message : null);
            var commitResult = db.Commit();
            Assert.True(commitResult.IsSuccess, commitResult.IsFailure ? commitResult.Error.Message : null);
        }

        var result = MsiIntegrityVerifier.Verify(msiPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(SignatureVerdict.Failed, result.Value.Verdict);
    }

    [Fact]
    public void Verify_PayloadTamperedAfterSigning_DetectsContentMismatch()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Unlike the envelope-tamper test above, this proves the SEPARATE content-binding check:
        // the signature itself stays cryptographically valid (it signs the declared hash), but the
        // declared hash no longer matches the actual file inside the compiled package. This is
        // exercised directly against the pure comparison function with a crafted mismatch, since
        // corrupting a real embedded cabinet's compressed bytes without invalidating the OLE
        // compound-file structure is not a reliable black-box operation; the extraction and hash
        // recomputation themselves are already proven live by every other test in this file
        // (they must correctly reproduce the ORIGINAL matching hash for Verified to occur at all).
        var declared = new List<ManifestFileEntry>
        {
            new() { Name = "app.exe", Sha256 = "AAAA" },
            new() { Name = "helper.dll", Sha256 = "BBBB" }
        };
        var actual = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app.exe"] = "FFFF", // tampered: hash no longer matches what was signed
            ["helper.dll"] = "BBBB"
        };

        var mismatches = MsiIntegrityVerifier.FindContentMismatches(declared, actual);

        Assert.Single(mismatches);
        Assert.Contains("app.exe", mismatches[0]);
    }

    [Fact]
    public void FindContentMismatches_FileMissingFromPayload_ReportsMismatch()
    {
        var declared = new List<ManifestFileEntry> { new() { Name = "ghost.exe", Sha256 = "AAAA" } };
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);

        var mismatches = MsiIntegrityVerifier.FindContentMismatches(declared, actual);

        Assert.Single(mismatches);
        Assert.Contains("ghost.exe", mismatches[0]);
    }

    [Fact]
    public void FindContentMismatches_EverythingMatches_ReturnsEmpty()
    {
        var declared = new List<ManifestFileEntry> { new() { Name = "app.exe", Sha256 = "AAAA" } };
        var actual = new Dictionary<string, string>(StringComparer.Ordinal) { ["app.exe"] = "aaaa" }; // case-insensitive

        var mismatches = MsiIntegrityVerifier.FindContentMismatches(declared, actual);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Verify_NonExistentMsi_ReturnsFailureResult()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var result = MsiIntegrityVerifier.Verify(
            Path.Combine(_tempDir, "nope.msi"), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(result.IsFailure);
    }

    private const string IntegrityTableEmitterFormatTag = "falkforge-ecdsa-envelope-v2";

    /// <summary>
    /// Reads the CLASSICAL (ECDSA-P256) signature's fingerprint. The zero-config Integrity() path
    /// is hybrid on a PQ-capable machine (an ephemeral ECDSA key plus an ephemeral ML-DSA companion
    /// — see <c>EcdsaManifestSigner.BuildProviders</c>), so <c>Signatures</c> may carry two entries;
    /// <see cref="IntegrityEnvelopeCodec.MatchTrustedSignature"/> only ever matches against the
    /// classical entry's fingerprint, so that is what a <c>--trusted-key</c> value must equal.
    /// </summary>
    private static string ReadFirstFingerprint(string msiPath)
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
}
