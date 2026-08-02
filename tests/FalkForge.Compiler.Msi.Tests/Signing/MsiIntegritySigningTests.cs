using System.Runtime.Versioning;
using System.Text.Json;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
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
        Environment.SetEnvironmentVariable("FAKESIGIL_ATTEST_SUCCEEDS", null);
        SigilDetector.Reset();
        if (Directory.Exists(_tempDir))
        {
            // Cleanup is best-effort: a locked file or transient I/O error must not fail the test.
            TestTemp.TryDelete(_tempDir);
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
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope),
            "The embedded ECDSA envelope must cryptographically verify against its own embedded key.");
        Assert.Contains(envelope.Files, f => f.Name == "app.exe");

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
    public void Compile_WithIntegrity_WhenSigilSubcommandFails_StillEmbedsEcdsaSignature()
    {
        // Deterministic CI coverage for the never-fatal SBOM-attestation contract: sigil being
        // reachable on PATH must not change or block the always-on ECDSA signature, even when its
        // sign-manifest/attest subcommands fail. CI has no reason to have real sigil installed, so this
        // does not depend on host state — the FakeSigil project (referenced purely for its build
        // output, never linked into this assembly's code) puts a `sigil.exe` right next to this test
        // assembly that answers `--version` successfully but fails every other subcommand, exactly
        // like a real but unconfigured sigil install.
        FakeSigilHarness.EnableDetection(_originalPath);

        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_WhenSigilSubcommandFails_StillEmbedsEcdsaSignature));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrityFakeSigilApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrityFakeSigilApp"));
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
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));

        // The fake sigil's sign-manifest/attest subcommands always fail (exit 1) — proving the SBOM
        // row/sidecar are genuinely optional and their absence never blocks the mandatory signature
        // above, deterministically, regardless of what is (or is not) installed on the host machine.
        Assert.DoesNotContain(rows, r => r[0] == "SbomAttestation");
        Assert.False(File.Exists(result.Value + ".attest.json"));
    }

    [Theory]
    [InlineData(SbomFormat.Spdx, "spdx")]
    [InlineData(SbomFormat.CycloneDx, "cyclonedx")]
    public void Compile_WithIntegrity_SbomAttestationFormatColumnDescribesTheDocumentActuallyEmbedded(
        SbomFormat requested, string expectedFormatTag)
    {
        // THE false-claim surface. The `Format` column of the SbomAttestation row is the only thing a
        // consumer of a shipped MSI can read to learn how to parse the attested predicate. Before this
        // fix it was derived from the configured SbomFormat while the document itself was produced by
        // a hardcoded CycloneDX writer, so an Integrity()-configured MSI — SPDX being the default —
        // shipped `Format="spdx"` over CycloneDX bytes.
        //
        // Asserting the tag alone would have passed throughout the entire lifetime of the bug, so this
        // reads the embedded document's OWN self-declaration out of the DSSE payload and requires the
        // two to agree. FakeSigil's attest-succeeds mode wraps the predicate verbatim as that payload,
        // so what is asserted here is byte-for-byte the SBOM the compiler generated.
        FakeSigilHarness.EnableAttesting(_originalPath);

        var (sourceFile, outputDir) = CreatePackageInputs($"format_{requested}");
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = $"SbomFormat{requested}App";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SbomFormatApp"));
            p.Integrity(i => i.Sbom(requested));
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var rows = ReadIntegrityRows(result.Value);
        var sbomRow = Assert.Single(rows, r => r[0] == "SbomAttestation");

        Assert.Equal(expectedFormatTag, sbomRow[1]);

        using var attestation = JsonDocument.Parse(sbomRow[2]!);
        var embedded = attestation.RootElement.GetProperty("payload");

        if (requested == SbomFormat.Spdx)
        {
            Assert.Equal("SPDX-2.3", embedded.GetProperty("spdxVersion").GetString());
            Assert.False(embedded.TryGetProperty("bomFormat", out _));

            // The SHA1 the whole SPDX path exists for, sourced from the packaged bytes.
            var checksums = embedded.GetProperty("files")[0].GetProperty("checksums")
                .EnumerateArray()
                .Select(c => c.GetProperty("algorithm").GetString())
                .ToList();
            Assert.Contains("SHA1", checksums);
            Assert.Contains("SHA256", checksums);
        }
        else
        {
            Assert.Equal("CycloneDX", embedded.GetProperty("bomFormat").GetString());
            Assert.False(embedded.TryGetProperty("spdxVersion", out _));
        }
    }

    [Fact]
    public void Compile_WithIntegrity_AndNoSbomFormatRequested_StillShipsCycloneDxBytesUnderACycloneDxTag()
    {
        // The compatibility guarantee of this branch, asserted end to end through MsiCompiler rather
        // than on the enum constant. Making SbomFormat finally select a writer changes what a
        // DEFAULT Integrity() build emits unless the default itself moves: every package built
        // before this branch shipped CycloneDX bytes (the writer was hardcoded), so an unchanged
        // Spdx default would have swapped the attested document's schema out from under every
        // existing consumer — and src/FalkForge.Cli/MsiInspector.cs shows the in-tree consumer
        // pattern, selecting `Data` and never reading `Format`.
        //
        // Both halves are asserted because either alone is satisfiable by the bug: the tag alone
        // passed throughout the lifetime of the original defect, and the bytes alone would not
        // catch a tag that misdescribes them.
        FakeSigilHarness.EnableAttesting(_originalPath);

        var (sourceFile, outputDir) = CreatePackageInputs("default_format");
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "SbomDefaultFormatApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SbomDefaultFormatApp"));
            // Deliberately no .Sbom(...) call — this is the default path.
            p.Integrity(i => { });
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var rows = ReadIntegrityRows(result.Value);
        var sbomRow = Assert.Single(rows, r => r[0] == "SbomAttestation");

        Assert.Equal("cyclonedx", sbomRow[1]);

        using var attestation = JsonDocument.Parse(sbomRow[2]!);
        var embedded = attestation.RootElement.GetProperty("payload");
        Assert.Equal("CycloneDX", embedded.GetProperty("bomFormat").GetString());
        Assert.False(embedded.TryGetProperty("spdxVersion", out _),
            "A default Integrity() build must not start emitting SPDX bytes.");
    }

    [Fact]
    public void Compile_WithIntegrity_RequestedSbomFormat_DoesNotChangeWhatTheSignatureDeclares()
    {
        // Packaging now decides, from the requested SbomFormat, whether to accumulate the per-file
        // SHA-1 that SPDX 2.3 §8.4 mandates (MsiAuthoring.ShouldCaptureSpdxFileChecksums). That makes
        // an SBOM setting reach into the packaging callbacks that also produce the SHA-256 the ECDSA
        // envelope signs — so this pins the blast radius: choosing a document format may narrow the
        // SBOM's descriptive input and NOTHING else. The signed declaration must be identical.
        //
        // Asserted on the envelope's own (name, sha256) set rather than on the signature bytes,
        // because ECDSA signing is deliberately nondeterministic (fresh nonce per call) — the same
        // payload signs differently every time by design, so comparing signatures would prove
        // nothing and comparing them for equality would fail for the wrong reason.
        // ONE source file, compiled twice. Giving each format its own file would compare two
        // different payloads and the equality below would be meaningless (it would also fail).
        var sourceDir = Path.Combine(_tempDir, "blastradius_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "fake executable content shared by both SBOM formats");

        var declarations = new List<List<(string Name, string Sha256)>>();

        foreach (var format in new[] { SbomFormat.Spdx, SbomFormat.CycloneDx })
        {
            var outputDir = Path.Combine(_tempDir, $"blastradius_{format}_output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "SbomBlastRadiusApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SbomBlastRadiusApp"));
                p.Integrity(i => i.Sbom(format));
            });

            var result = new MsiCompiler().Compile(package, outputDir);
            Assert.True(result.IsSuccess, $"Compile failed for {format}: {(result.IsFailure ? result.Error.Message : "")}");

            var manifestRow = Assert.Single(ReadIntegrityRows(result.Value), r => r[0] == "ManifestSignature");
            var envelope = IntegrityEnvelopeCodec.Parse(manifestRow[2]!);
            Assert.NotNull(envelope);
            Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope),
                $"The {format} build's envelope must still verify against its own embedded key.");

            declarations.Add(envelope.Files
                .Select(f => (f.Name, f.Sha256))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToList());
        }

        // Non-empty first: two empty declarations are trivially equal, and an empty one is exactly the
        // failure mode IntegritySigner.BuildPayloadHashEntries exists to prevent.
        Assert.NotEmpty(declarations[0]);
        Assert.Equal(declarations[0], declarations[1]);
    }

    [Fact]
    public void Compile_WithIntegrity_AndNotConfigured_HasNoIntegrityTable()
    {
        // Negative case: without Integrity() at all, the _FalkForgeIntegrity table must not exist —
        // no table, no rows, nothing to accidentally read as "signed".
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithIntegrity_AndNotConfigured_HasNoIntegrityTable));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NoIntegrityApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "NoIntegrityApp"));
            // No .Integrity(...) call.
        });

        var compiler = new MsiCompiler();
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, dbResult.IsFailure ? dbResult.Error.Message : null);
        using var db = dbResult.Value;
        var rowsResult = db.QueryRows("SELECT `Id`, `Format`, `Data` FROM `_FalkForgeIntegrity`", 3);
        Assert.True(rowsResult.IsFailure, "Expected no _FalkForgeIntegrity table when Integrity() is never configured.");
        Assert.False(File.Exists(result.Value + ".sig.json"));
    }
}
