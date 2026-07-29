using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Models;
using FalkForge.Signing;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Signing;

/// <summary>
/// The SBOM <b>attestation</b> must attest the bytes the cabinet actually packaged, not whatever
/// sits at <c>ResolvedFile.SourcePath</c> by the time integrity signing runs.
///
/// <para>The sidecar SBOM was fixed for this first (see
/// <c>CabinetBuilderSbomToctouTests</c>): <see cref="CabinetBuilder.BuildCabinet"/> captures each
/// file's digest inside the native FCI callbacks, at the moment the compressor consumes the bytes.
/// The attestation path — the strictly more trust-critical of the two artefacts, since it is a
/// signed DSSE claim rather than a plain sidecar — was still re-opening
/// <c>ResolvedFile.SourcePath</c> and hashing whatever it found. Anything that touches a source
/// file between "cabinet built" (step 5) and "integrity signed" (step 8.5) — a racing build step,
/// an AV rescan rewrite, an attacker with write access to the build tree — silently desynced the
/// attestation from the shipped package.</para>
///
/// <para><b>How these tests reach the code.</b> They call <c>IntegritySigner.SignAndEmbed</c>
/// directly — the same internal entry point <c>MsiAuthoring</c> step 8.5 calls, with the same
/// arguments — rather than driving <c>MsiCompiler.Compile</c> end to end. That is deliberate and
/// necessary: the mutation being simulated happens <i>between</i> two steps of a single
/// <c>Compile</c> call, and the public API exposes no seam to interpose it. This mirrors how
/// <c>CabinetBuilderSbomToctouTests</c> proves the same property for the sidecar. No reflection is
/// involved.</para>
///
/// <para>Shares the "SigilProcess" collection with <c>MsiIntegritySigningTests</c> and
/// <c>SigilDetectorTests</c>: all three mutate the process-wide <c>PATH</c> and the process-wide
/// <see cref="SigilDetector"/> cache, so xUnit must never run them concurrently.</para>
/// </summary>
[Collection("SigilProcess")]
[SupportedOSPlatform("windows")]
public sealed class IntegrityAttestationSbomToctouTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"AttestToctou_{Guid.NewGuid():N}");
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");

    public IntegrityAttestationSbomToctouTests()
    {
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("FAKESIGIL_ATTEST_SUCCEEDS", null);
        Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", null);
        SigilDetector.Reset();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void SignAndEmbed_SourceMutatedAfterCabinetBuild_AttestsPackagedBytesNotMutatedBytes()
    {
        EnableAttestingFakeSigil();

        var packagedBytes = "packaged content"u8.ToArray();
        var sourcePath = Path.Combine(_tempDir, "payload.bin");
        File.WriteAllBytes(sourcePath, packagedBytes);
        var packagedHash = Convert.ToHexString(SHA256.HashData(packagedBytes));

        var files = new[] { MakeResolvedFile(sourcePath, "payload.bin", "C_payload", "F_payload") };

        using var cabBuilder = new CabinetBuilder();
        var cabResult = cabBuilder.BuildCabinet(files, Path.Combine(_tempDir, "cab"), CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, cabResult.IsFailure ? cabResult.Error.Message : "");

        // The cabinet's contents are frozen from here on. Only the on-disk source changes — exactly
        // what a racing build step or a write-capable attacker would do in the window before signing.
        var mutatedBytes = "TAMPERED AFTER PACKAGING"u8.ToArray();
        File.WriteAllBytes(sourcePath, mutatedBytes);
        var mutatedHash = Convert.ToHexString(SHA256.HashData(mutatedBytes));

        var msiPath = CompileHostMsi("single", sourcePath);
        var package = BuildIntegrityPackage("AttestToctouApp", sourcePath);

        var signResult = IntegritySigner.SignAndEmbed(msiPath, package, files, cabBuilder.PackagedFileHashes);

        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : "");

        var components = ReadAttestedComponents(msiPath);
        var component = Assert.Single(components);
        Assert.Equal("payload.bin", component.Name);
        Assert.Equal(packagedHash, component.Sha256Hash);
        Assert.NotEqual(mutatedHash, component.Sha256Hash);
    }

    [Fact]
    public void SignAndEmbed_TwoFilesShareOneSourcePath_AttestsBothFileIdsWithPackagedBytes()
    {
        // Two File-table rows may legitimately point at one on-disk source (a shared DLL or licence
        // shipped into two components). The packaged-hash map is keyed by FileId — the File table's
        // own unique identity — precisely because SourcePath is not unique. Keying the attestation
        // lookup on SourcePath instead would find nothing in that map and silently drop every
        // component from the signed SBOM, so this guards the key choice as well as the digest source.
        EnableAttestingFakeSigil();

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
        var package = BuildIntegrityPackage("AttestSharedSourceApp", sourcePath);

        var signResult = IntegritySigner.SignAndEmbed(msiPath, package, files, cabBuilder.PackagedFileHashes);

        Assert.True(signResult.IsSuccess, signResult.IsFailure ? signResult.Error.Message : "");

        var components = ReadAttestedComponents(msiPath);
        Assert.Equal(2, components.Count);
        Assert.Contains(components, c => c.Name == "shared1.dll");
        Assert.Contains(components, c => c.Name == "shared2.dll");
        Assert.All(components, c => Assert.Equal(packagedHash, c.Sha256Hash));
    }

    [Fact]
    public void SignAndEmbed_FileMissingFromPackagedHashes_FailsTheBuildAndEmitsNoSignatureOrAttestation()
    {
        // Supersedes the original expectation ("...IsSkippedRatherThanReRead"), which asserted the
        // attestation simply omitted the unreported file. That was correct while only the SBOM
        // sourced packaged digests. Now that the ECDSA signature does too (see
        // IntegritySignaturePayloadHashToctouTests), a missing packaging digest is fatal at step 1
        // of SignAndEmbed — a signature's declared set defines what is covered, so quietly narrowing
        // it is a real weakening, unlike under-reporting a descriptive SBOM. The attestation is
        // produced at step 2 and is therefore never reached.
        //
        // What still holds, and is what this test now pins: a file the cabinet never reported a
        // digest for is NEVER absorbed into a signed artefact by re-reading its source. The outcome
        // is simply stronger than before — nothing is emitted at all rather than an SBOM that
        // silently under-reports. GenerateSbomForAttestation keeps its own skip as a local guard;
        // this test asserts the composite behaviour callers actually observe.
        EnableAttestingFakeSigil();

        var packagedSource = Path.Combine(_tempDir, "packaged.bin");
        File.WriteAllBytes(packagedSource, "packaged content"u8.ToArray());

        var unpackagedSource = Path.Combine(_tempDir, "never-packaged.bin");
        File.WriteAllBytes(unpackagedSource, "present on disk but never packaged"u8.ToArray());

        var packagedFiles = new[] { MakeResolvedFile(packagedSource, "packaged.bin", "C_packaged", "F_packaged") };

        using var cabBuilder = new CabinetBuilder();
        var cabResult = cabBuilder.BuildCabinet(packagedFiles, Path.Combine(_tempDir, "cab3"), CompressionLevel.High);
        Assert.True(cabResult.IsSuccess, cabResult.IsFailure ? cabResult.Error.Message : "");

        // The signer is handed a file the cabinet never saw, alongside one it did.
        var signedFiles = new[]
        {
            packagedFiles[0],
            MakeResolvedFile(unpackagedSource, "never-packaged.bin", "C_ghost", "F_ghost"),
        };

        var msiPath = CompileHostMsi("missing", packagedSource);
        var package = BuildIntegrityPackage("AttestMissingHashApp", packagedSource);

        var signResult = IntegritySigner.SignAndEmbed(msiPath, package, signedFiles, cabBuilder.PackagedFileHashes);

        Assert.True(signResult.IsFailure, "A payload file with no packaging-time digest must abort the build.");
        Assert.Equal(ErrorKind.IntegrityError, signResult.Error.Kind);

        // The attesting fake sigil is on PATH and would have produced a sidecar had step 2 run, so
        // its absence proves the ghost file never reached the attestation — not merely that sigil
        // was unavailable.
        Assert.False(File.Exists(msiPath + ".attest.json"),
            "No attestation may be emitted when the payload set could not be established.");
        Assert.False(File.Exists(msiPath + ".sig.json"),
            "No signature sidecar may be emitted when the payload set could not be established.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed record AttestedComponent(string? Name, string? Sha256Hash);

    /// <summary>
    /// Reads back the SBOM the compiler asked sigil to attest. The FakeSigil double in
    /// FAKESIGIL_ATTEST_SUCCEEDS mode wraps the predicate document verbatim as the DSSE envelope's
    /// <c>payload</c>, so this is the exact SBOM <c>GenerateSbomForAttestation</c> produced.
    /// </summary>
    private static List<AttestedComponent> ReadAttestedComponents(string msiPath)
    {
        var attestPath = msiPath + ".attest.json";
        Assert.True(File.Exists(attestPath),
            $"Expected an attestation sidecar at '{attestPath}' — the attesting fake sigil must have run.");

        using var doc = JsonDocument.Parse(File.ReadAllText(attestPath));
        return doc.RootElement
            .GetProperty("payload")
            .GetProperty("components")
            .EnumerateArray()
            .Select(c => new AttestedComponent(
                c.GetProperty("name").GetString(),
                c.GetProperty("hashes").EnumerateArray().Single().GetProperty("content").GetString()))
            .ToList();
    }

    /// <summary>
    /// Puts the FakeSigil test double on PATH in its opt-in attest-succeeds mode. Without a sigil
    /// whose <c>attest</c> subcommand succeeds, <c>IntegritySigner</c> swallows the failure by design
    /// and no attestation is ever written, leaving nothing to assert on. The default
    /// everything-fails behaviour of that same double (and the never-fatal-SBOM contract test that
    /// depends on it) is untouched — the success mode is env-var gated.
    /// </summary>
    private void EnableAttestingFakeSigil()
    {
        var binDir = new DirectoryInfo(AppContext.BaseDirectory);       // .../bin/<Config>/<TFM>
        var configDir = binDir.Parent ?? throw new DirectoryNotFoundException();
        var projectDir = configDir.Parent?.Parent ?? throw new DirectoryNotFoundException(); // .../<ThisProject>
        var testsRoot = projectDir.Parent ?? throw new DirectoryNotFoundException();          // .../tests
        var fakeSigilDir = Path.Combine(
            testsRoot.FullName, "FalkForge.Compiler.Msi.Tests.FakeSigil", "bin", configDir.Name, binDir.Name);

        Assert.True(File.Exists(Path.Combine(fakeSigilDir, "sigil.exe")),
            $"Test setup invariant: FakeSigil build output not found at '{fakeSigilDir}'.");

        Environment.SetEnvironmentVariable("FAKESIGIL_ATTEST_SUCCEEDS", "1");
        Environment.SetEnvironmentVariable("PATH", fakeSigilDir + Path.PathSeparator + _originalPath);
        SigilDetector.Reset();
        Assert.True(SigilDetector.IsAvailable(), "Test setup invariant: the fake sigil.exe must answer --version.");
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
    /// table into, with signing switched off so the compile itself performs no attestation. The
    /// <see cref="ResolvedFile"/> list handed to <c>SignAndEmbed</c> is what is under test here —
    /// <c>MsiAuthoring</c> passes its own <c>resolved.Files</c> in exactly the same way; this MSI only
    /// supplies a genuine database so the non-reproducible in-band embed path runs too.
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
