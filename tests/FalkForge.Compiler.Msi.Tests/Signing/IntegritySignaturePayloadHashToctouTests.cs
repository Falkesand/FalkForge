using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Models;
using FalkForge.Signing;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Signing;

/// <summary>
/// The ECDSA manifest <b>signature</b> must commit to the bytes the cabinet actually packaged, not
/// to whatever sits at <c>ResolvedFile.SourcePath</c> by the time integrity signing runs.
///
/// <para>This is the signature-path counterpart of
/// <see cref="IntegrityAttestationSbomToctouTests"/>. The attestation SBOM was fixed first;
/// <c>BuildPayloadHashEntries</c> — which produces the <c>(fileName, sha256)</c> pairs
/// <see cref="EcdsaManifestSigner"/> signs — was still re-opening the source path at step 8.5, long
/// after step 5 froze the cabinet's contents.</para>
///
/// <para><b>Why this matters even though the old behaviour was fail-closed.</b>
/// <c>FalkForge.Cli.MsiIntegrityVerifier</c> recomputes each payload digest by re-extracting the
/// MSI's <i>embedded cabinets</i>, so a signature over re-read source bytes simply failed to verify
/// — no trust bypass, but a legitimate build whose source changed mid-build shipped an MSI that its
/// own verifier rejected, with a "hash mismatch" that pointed at nothing an operator could act on.
/// Sourcing both halves from the packaged bytes makes signer and verifier agree by construction.
/// </para>
///
/// <para><b>Why a missing packaging digest is fatal here, unlike in the SBOM.</b> The SBOM and the
/// attestation skip a file the cabinet never reported — under-reporting a descriptive inventory is
/// safe. A signature is not descriptive but prescriptive: its declared set defines what is covered,
/// so dropping a file narrows that set with nothing in the artifact disclosing it. That unannounced
/// narrowing is the reason for the hard fail, on its own.</para>
///
/// <para><b>Stated precisely, because the reachability is easy to overstate.</b>
/// <c>MsiIntegrityVerifier.FindContentMismatches</c> is BIDIRECTIONAL — a declared file absent from
/// the actual payload and an actual payload file absent from the declaration are both mismatches —
/// so under the default embedded-cabinet layout a dropped file is caught at once ("present in the
/// MSI's embedded payload but not signed"). Only <c>ReadActualPayloadHashes</c> is one-sided: it
/// skips any <c>Media.Cabinet</c> without the <c>#</c> prefix, so under an external-cabinet layout
/// (<c>MediaTemplate</c> with <c>EmbedCabinet = false</c>, a whole-package setting) the actual set
/// is empty. Even there a PARTIAL drop still fails, because every remaining declared entry is then
/// reported "not found in the MSI's embedded payload". The one case that reports VERIFIED over an
/// unchecked file is the narrow one where the dropped file was the ONLY declared payload file, which
/// leaves an empty declaration matching an empty actual set. That corner is real but small; the
/// build fails loud because of the narrowing, not because of the corner.</para>
///
/// <para><b>How these tests reach the code.</b> They call <c>IntegritySigner.SignAndEmbed</c>
/// directly — the same internal entry point <c>MsiAuthoring</c> step 8.5 calls, with the same
/// arguments — because the mutation being simulated happens <i>between</i> two steps of a single
/// <c>Compile</c> call and the public API exposes no seam to interpose it. Identical rationale to
/// <see cref="IntegrityAttestationSbomToctouTests"/>; no reflection is involved.</para>
///
/// <para>Shares the "SigilProcess" collection with the other integrity-signing suites: they all
/// mutate process-wide state (<c>PATH</c>, <c>FALKFORGE_NO_SIGN</c>, the
/// <see cref="SigilDetector"/> cache), so xUnit must never run them concurrently.</para>
/// </summary>
[Collection("SigilProcess")]
[SupportedOSPlatform("windows")]
public sealed class IntegritySignaturePayloadHashToctouTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"SigToctou_{Guid.NewGuid():N}");

    public IntegritySignaturePayloadHashToctouTests()
    {
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void SignAndEmbed_SourceMutatedAfterCabinetBuild_SignsPackagedBytesNotMutatedBytes()
    {
        var packagedBytes = "packaged content"u8.ToArray();
        var sourcePath = Path.Combine(_tempDir, "payload.bin");
        File.WriteAllBytes(sourcePath, packagedBytes);
        var packagedHash = Convert.ToHexString(SHA256.HashData(packagedBytes));

        var files = new[] { MakeResolvedFile(sourcePath, "payload.bin", "C_payload", "F_payload") };

        using var cabBuilder = new CabinetBuilder();
        var cabResult = cabBuilder.BuildCabinet(files, Path.Combine(_tempDir, "cab"), CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, cabResult.IsFailure ? cabResult.Error.Message : "");

        // The cabinet's contents are frozen from here on; only the on-disk source changes — exactly
        // what a racing build step or a write-capable attacker would do before signing runs.
        var mutatedBytes = "TAMPERED AFTER PACKAGING"u8.ToArray();
        File.WriteAllBytes(sourcePath, mutatedBytes);
        var mutatedHash = Convert.ToHexString(SHA256.HashData(mutatedBytes));

        var msiPath = CompileHostMsi("single", sourcePath);
        var package = BuildIntegrityPackage("SigToctouApp", sourcePath);

        var signResult = IntegritySigner.SignAndEmbed(msiPath, package, files, cabBuilder.PackagedFileHashes);

        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : "");

        var declared = ReadSignedDeclaration(msiPath);
        var entry = Assert.Single(declared);
        Assert.Equal("payload.bin", entry.Name);
        Assert.Equal(packagedHash, entry.Sha256);
        Assert.NotEqual(mutatedHash, entry.Sha256);
    }

    [Fact]
    public void SignAndEmbed_TwoFilesShareOneSourcePath_SignsBothFileIdsWithPackagedBytes()
    {
        // Two File-table rows may legitimately point at one on-disk source (a shared DLL or licence
        // shipped into two components). The packaged-hash map is keyed by FileId — the File table's
        // own unique identity — precisely because SourcePath is not unique. Keying the signature
        // lookup on SourcePath instead would find nothing in that map, which under the fail-loud
        // rule below would abort every such build; this pins the key choice as well as the digest
        // source. The two entries keep distinct FileNames because the envelope's declaration is
        // name-only granularity and MsiIntegrityVerifier treats a duplicate name as tamper.
        var packagedBytes = "shared payload bytes"u8.ToArray();
        var sourcePath = Path.Combine(_tempDir, "shared.dll");
        File.WriteAllBytes(sourcePath, packagedBytes);
        var packagedHash = Convert.ToHexString(SHA256.HashData(packagedBytes));

        var files = new[]
        {
            MakeResolvedFile(sourcePath, "shared1.dll", "C_shared1", "F_shared1"),
            MakeResolvedFile(sourcePath, "shared2.dll", "C_shared2", "F_shared2"),
        };

        using var cabBuilder = new CabinetBuilder();
        var cabResult = cabBuilder.BuildCabinet(files, Path.Combine(_tempDir, "cab2"), CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, cabResult.IsFailure ? cabResult.Error.Message : "");

        File.WriteAllBytes(sourcePath, "TAMPERED AFTER PACKAGING"u8.ToArray());

        var msiPath = CompileHostMsi("shared", sourcePath);
        var package = BuildIntegrityPackage("SigSharedSourceApp", sourcePath);

        var signResult = IntegritySigner.SignAndEmbed(msiPath, package, files, cabBuilder.PackagedFileHashes);

        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : "");

        var declared = ReadSignedDeclaration(msiPath);
        Assert.Equal(2, declared.Count);
        Assert.Contains(declared, e => e.Name == "shared1.dll");
        Assert.Contains(declared, e => e.Name == "shared2.dll");
        Assert.All(declared, e => Assert.Equal(packagedHash, e.Sha256));
    }

    [Fact]
    public void SignAndEmbed_FileMissingFromPackagedHashes_FailsRatherThanSigningANarrowerSet()
    {
        // Deliberately the OPPOSITE of the SBOM/attestation miss-path rule. Silently omitting a
        // payload file from the signed declaration narrows what the signature covers, and nothing in
        // the emitted artifact discloses that it was narrowed — that is the weakening this fail-loud
        // exists to prevent. (Under the default embedded-cabinet layout the verifier's second,
        // actual-to-declared direction would still catch the dropped file; the narrow case where a
        // drop survives as VERIFIED is an external-cabinet layout in which the dropped file was the
        // only declared payload file. See the class doc — the rule does not depend on that corner.)
        // Re-reading the source to fill the gap is the very bug this branch removes. That leaves
        // failing the build: in a real compile every resolved file passes through CabinetPlanner and
        // any FCIAddFile failure already aborts, so a missing digest can only mean a broken invariant.
        var packagedBytes = "packaged content"u8.ToArray();
        var packagedSource = Path.Combine(_tempDir, "packaged.bin");
        File.WriteAllBytes(packagedSource, packagedBytes);

        var unpackagedSource = Path.Combine(_tempDir, "never-packaged.bin");
        File.WriteAllBytes(unpackagedSource, "present on disk but never packaged"u8.ToArray());

        var packagedFiles = new[] { MakeResolvedFile(packagedSource, "packaged.bin", "C_packaged", "F_packaged") };

        using var cabBuilder = new CabinetBuilder();
        var cabResult = cabBuilder.BuildCabinet(packagedFiles, Path.Combine(_tempDir, "cab3"), CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, cabResult.IsFailure ? cabResult.Error.Message : "");

        // The signer is handed a file the cabinet never saw, alongside one it did. The ghost file
        // still exists on disk, so a source re-read would happily produce a digest for it.
        var signedFiles = new[]
        {
            packagedFiles[0],
            MakeResolvedFile(unpackagedSource, "never-packaged.bin", "C_ghost", "F_ghost"),
        };

        var msiPath = CompileHostMsi("missing", packagedSource);
        var package = BuildIntegrityPackage("SigMissingHashApp", packagedSource);

        var signResult = IntegritySigner.SignAndEmbed(msiPath, package, signedFiles, cabBuilder.PackagedFileHashes);

        Assert.True(signResult.IsFailure, "A payload file with no packaging-time digest must abort the build.");
        Assert.Equal(ErrorKind.IntegrityError, signResult.Error.Kind);
        Assert.Contains("F_ghost", signResult.Error.Message, StringComparison.Ordinal);
        Assert.Contains("never-packaged.bin", signResult.Error.Message, StringComparison.Ordinal);

        // Nothing was signed: no narrower declaration reached the sidecar or the MSI.
        Assert.False(File.Exists(msiPath + ".sig.json"),
            "The signature sidecar must not be written when the payload set could not be established.");
    }

    [Fact]
    public void SignAndEmbed_SourceDeletedAfterCabinetBuild_StillSignsThePackagedBytes()
    {
        // The old implementation skipped any file whose SourcePath no longer existed, which is the
        // same silent narrowing as above and directly reachable by anyone with write access to the
        // build tree: delete a source file after step 5 and it ships packaged but unsigned. The
        // packaged digest was captured while FCI read the bytes, so deletion afterwards is
        // irrelevant to what the signature can honestly claim.
        var packagedBytes = "bytes that outlive their source file"u8.ToArray();
        var sourcePath = Path.Combine(_tempDir, "vanishing.bin");
        File.WriteAllBytes(sourcePath, packagedBytes);
        var packagedHash = Convert.ToHexString(SHA256.HashData(packagedBytes));

        var files = new[] { MakeResolvedFile(sourcePath, "vanishing.bin", "C_vanish", "F_vanish") };

        using var cabBuilder = new CabinetBuilder();
        var cabResult = cabBuilder.BuildCabinet(files, Path.Combine(_tempDir, "cab4"), CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, cabResult.IsFailure ? cabResult.Error.Message : "");

        var msiPath = CompileHostMsi("deleted", sourcePath);
        var package = BuildIntegrityPackage("SigDeletedSourceApp", sourcePath);

        File.Delete(sourcePath);

        var signResult = IntegritySigner.SignAndEmbed(msiPath, package, files, cabBuilder.PackagedFileHashes);

        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : "");

        var declared = ReadSignedDeclaration(msiPath);
        var entry = Assert.Single(declared);
        Assert.Equal("vanishing.bin", entry.Name);
        Assert.Equal(packagedHash, entry.Sha256);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads back the exact <c>(name, sha256)</c> pairs the ECDSA envelope committed to, from the
    /// '&lt;msi&gt;.sig.json' sidecar <c>SignAndEmbed</c> always writes. Also asserts the envelope
    /// still self-verifies, so a declaration that was somehow rewritten after signing cannot pass.
    /// </summary>
    private static IReadOnlyList<ManifestFileEntry> ReadSignedDeclaration(string msiPath)
    {
        var sidecarPath = msiPath + ".sig.json";
        Assert.True(File.Exists(sidecarPath), $"Expected a signature sidecar at '{sidecarPath}'.");

        var envelope = IntegrityEnvelopeCodec.Parse(File.ReadAllText(sidecarPath));
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope),
            "The signature must verify against its own declaration before its contents are asserted on.");

        return envelope.Files;
    }

    private static ResolvedFile MakeResolvedFile(string sourcePath, string fileName, string componentId, string fileId)
        => new()
        {
            SourcePath = sourcePath,
            TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
            FileName = fileName,
            FileSize = new FileInfo(sourcePath).Length,
            ComponentId = componentId,
            FileId = fileId,
        };

    private static PackageModel BuildIntegrityPackage(string name, string sourcePath)
        => InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourcePath).To(KnownFolder.ProgramFiles / "TestCorp" / name));
            p.Integrity(i => { });
        });

    /// <summary>
    /// Compiles a real MSI for <c>SignAndEmbed</c> to reopen and commit its <c>_FalkForgeIntegrity</c>
    /// table into, with signing switched off so the compile itself signs nothing. The
    /// <see cref="ResolvedFile"/> list handed to <c>SignAndEmbed</c> is what is under test;
    /// this MSI only supplies a genuine database so the in-band embed path runs too.
    /// </summary>
    private string CompileHostMsi(string label, string sourcePath)
    {
        var outputDir = Path.Combine(_tempDir, $"{label}_output");
        Directory.CreateDirectory(outputDir);

        var package = BuildIntegrityPackage($"HostMsi{label}", sourcePath);

        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", "1");
        try
        {
            var result = new MsiCompiler().Compile(package, outputDir);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
            return result.Value;
        }
        finally
        {
            Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        }
    }
}
