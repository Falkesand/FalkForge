using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Signing;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Signing;

/// <summary>
/// MSI integrity signing must not silently depend on the external <c>sigil</c> CLI. Before this fix,
/// <c>MsiAuthoring</c> step 8.5 additionally gated on <c>SigilDetector.IsAvailable()</c>: an
/// <c>Integrity()</c>-configured build on a machine without sigil on PATH shipped a completely unsigned
/// MSI with zero warning. The bundle compiler never had this problem (its <c>EcdsaManifestSigner</c> path
/// is pure .NET); these tests prove the MSI compiler now signs the identical way — pure-.NET ECDSA always
/// runs, sigil's DSSE SBOM attestation is a strictly-additive, never-fatal extra.
///
/// <para>Shares a serialized xUnit collection with <see cref="SigilDetectorTests"/>: both test classes
/// touch the process-wide <see cref="SigilDetector"/> cache and (here) the process-wide <c>PATH</c>
/// environment variable, so they must never run concurrently with each other.</para>
/// </summary>
[Collection("SigilProcess")]
[SupportedOSPlatform("windows")]
public sealed class MsiIntegritySigningTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"MsiIntegrityTest_{Guid.NewGuid():N}");
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");

    public MsiIntegritySigningTests()
    {
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        SigilDetector.Reset();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private (string sourceFile, string outputDir) CreatePackageInputs(string label)
    {
        var sourceDir = Path.Combine(_tempDir, $"{label}_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"fake executable content for {label}");

        var outputDir = Path.Combine(_tempDir, $"{label}_output");
        Directory.CreateDirectory(outputDir);

        return (sourceFile, outputDir);
    }

    private static List<string?[]> ReadIntegrityRows(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        Assert.True(dbResult.IsSuccess, dbResult.IsFailure ? dbResult.Error.Message : null);
        using var db = dbResult.Value;

        var rowsResult = db.QueryRows("SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`", 3);
        Assert.True(rowsResult.IsSuccess, rowsResult.IsFailure ? rowsResult.Error.Message : null);
        return rowsResult.Value;
    }

    [Fact]
    public void Compile_WithIntegrity_SignsWithEcdsa_EvenWhenSigilIsNotOnPath()
    {
        // Force sigil unreachable regardless of what is actually installed on the host machine (sigil
        // IS present on some dev boxes), so this test proves the pure-.NET path unconditionally, not
        // just "happens to pass here".
        Environment.SetEnvironmentVariable("PATH", string.Empty);
        SigilDetector.Reset();
        Assert.False(SigilDetector.IsAvailable(), "Test setup invariant: sigil must be unreachable with an empty PATH.");

        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_SignsWithEcdsa_EvenWhenSigilIsNotOnPath));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrityNoSigilApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrityNoSigilApp"));
            p.Integrity(i => { });
        });

        var compiler = new MsiCompiler();
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var rows = ReadIntegrityRows(result.Value);
        var manifestRow = Assert.Single(rows, r => r[0] == "ManifestSignature");
        Assert.Equal(IntegrityTableEmitter.ManifestSignatureFormat, manifestRow[1]);
        Assert.NotEqual("sigil-manifest-v1", manifestRow[1]); // the old, no-longer-accurate format tag

        var envelope = IntegrityEnvelopeCodec.Parse(manifestRow[2]!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!),
            "The embedded ECDSA envelope must cryptographically verify against its own embedded key.");
        Assert.Contains(envelope!.Files, f => f.Name == "app.exe");

        // Without sigil, no SBOM attestation can be produced — but that must never have blocked the
        // signature above.
        Assert.DoesNotContain(rows, r => r[0] == "SbomAttestation");

        // The sidecar signature file is always written too, mirroring the embedded table row.
        Assert.True(File.Exists(result.Value + ".sig.json"));
    }

    [Fact]
    public void Compile_WithIntegrity_AndNoSignEnvVar_SkipsSigningEntirely()
    {
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_AndNoSignEnvVar_SkipsSigningEntirely));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrityNoSignApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrityNoSignApp"));
            p.Integrity(i => { });
        });

        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", "1");
        try
        {
            var compiler = new MsiCompiler();
            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

            // No _FalkForgeIntegrity table at all — the explicit opt-out still fully disables signing,
            // exactly like the bundle side's FALKFORGE_NO_SIGN handling.
            var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
            using var db = dbResult.Value;
            var rowsResult = db.QueryRows("SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`", 3);
            Assert.True(rowsResult.IsFailure, "Expected no _FalkForgeIntegrity table when FALKFORGE_NO_SIGN is set.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        }
    }

    [Fact]
    public void Compile_WithIntegrity_WhenSigilIsOnPath_StillEmbedsEcdsaSignature()
    {
        // The inverse of the primary test above: sigil ON path must not change or block the always-on
        // ECDSA signature either. Whether the opportunistic SBOM attestation sub-step actually succeeds
        // depends on sigil having a fully configured signing identity, not merely being reachable on
        // PATH — proven live by this very sandbox, where `sigil --version` succeeds but `sigil attest`
        // fails for lack of configuration, and IntegritySigner correctly swallows that (never-fatal
        // contract) rather than failing the build or the already-computed signature. So this test
        // intentionally does not require the SbomAttestation row to exist — only that IF it exists, its
        // sidecar is consistent, and that the mandatory ManifestSignature row is unaffected either way.
        SigilDetector.Reset();
        if (!SigilDetector.IsAvailable())
            return; // sigil is not installed on this machine — the always-on ECDSA path is already
                     // proven unconditionally by Compile_WithIntegrity_SignsWithEcdsa_EvenWhenSigilIsNotOnPath;
                     // there is nothing further to prove here without a real sigil binary on PATH.

        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_WhenSigilIsOnPath_StillEmbedsEcdsaSignature));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrityWithSigilApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrityWithSigilApp"));
            p.Integrity(i => { });
        });

        var compiler = new MsiCompiler();
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var rows = ReadIntegrityRows(result.Value);
        var manifestRow = Assert.Single(rows, r => r[0] == "ManifestSignature");
        Assert.Equal(IntegrityTableEmitter.ManifestSignatureFormat, manifestRow[1]);
        var envelope = IntegrityEnvelopeCodec.Parse(manifestRow[2]!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!));

        if (rows.Any(r => r[0] == "SbomAttestation"))
            Assert.True(File.Exists(result.Value + ".attest.json"));
    }
}
