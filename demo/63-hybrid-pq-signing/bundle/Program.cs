using System.Security.Cryptography;
using System.Text.Json;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Signing;

// Demo 63 -- Hybrid Post-Quantum Signing (ML-DSA / FIPS 204)
//
// A hybrid-signed bundle carries TWO signatures over the same manifest message: the
// classical ECDSA-P256 signature (what every engine verifies today) plus an ML-DSA-65
// companion signature that stays sound even against a future quantum adversary. The
// fluent entry point is one call:
//
//     .Integrity(i => i.HybridKey("classical.pem", "mldsa.pem"))
//
// (or, from a forge JSON config: "signing": { "provider": "pem",
//  "keyPath": "classical.pem", "pqKeyPath": "mldsa.pem" })
//
// The anti-strip guarantee: the "this publisher is hybrid" fact is pinned in the
// VERIFYING engine (EngineTrustAnchor.TrustHybridKey / the PqFingerprint= trusted-key
// metadata), never declared by the bundle itself. An attacker who strips the ML-DSA
// entry from the envelope leaves a classical signature whose pinned companion cannot
// be satisfied -- the engine rejects with INT011. This demo proves both halves with
// the real envelope verifier: the hybrid bundle verifies, the stripped copy fails.
//
// ML-DSA needs OS support (Windows 11 with the PQC CNG additions). On an older OS this
// demo fails loud up front instead of quietly demonstrating something else.

return Installer.BuildBundle(args, outputPath =>
{
    if (!MLDsa.IsSupported)
    {
        Console.WriteLine("ML-DSA (FIPS 204) is not supported by this machine's OS/crypto stack.");
        Console.WriteLine("Hybrid signing requires a build machine with ML-DSA support (SGN011) -- see README.");
        return Result<string>.Failure(ErrorKind.SecurityError,
            "SGN011: ML-DSA signing is not supported on this machine; the hybrid signing demo cannot run.");
    }

    var tempDir = Directory.CreateTempSubdirectory("falk-demo63-").FullName;
    try
    {
        var msiResult = BuildPayloadMsi(tempDir);
        if (!msiResult.IsSuccess)
            return Result<string>.Failure(msiResult.Error);
        var msiPath = msiResult.Value;

        // ──────────────────────────────────────────────────────────────
        // 1. The publisher's hybrid identity: a classical ECDSA-P256 key
        //    plus its ML-DSA-65 companion. In production these live in
        //    secured key storage; the demo generates a fresh pair.
        // ──────────────────────────────────────────────────────────────
        var classicalPem = Path.Combine(tempDir, "classical-signing-key.pem");
        var pqPem = Path.Combine(tempDir, "mldsa-signing-key.pem");
        string classicalFingerprint;
        string pqFingerprint;
        using (var classicalKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        using (var pqKey = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65))
        {
            File.WriteAllText(classicalPem, classicalKey.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(pqPem, pqKey.ExportPkcs8PrivateKeyPem());
            classicalFingerprint = IntegrityEnvelopeCodec.ComputeFingerprint(classicalKey.ExportSubjectPublicKeyInfo());
            pqFingerprint = IntegrityEnvelopeCodec.ComputeFingerprint(pqKey.ExportSubjectPublicKeyInfo());
        }

        Console.WriteLine("Hybrid publisher identity:");
        Console.WriteLine($"  classical (ECDSA-P256) fingerprint: {classicalFingerprint}");
        Console.WriteLine($"  companion (ML-DSA-65)  fingerprint: {pqFingerprint}");
        Console.WriteLine();

        // ──────────────────────────────────────────────────────────────
        // 2. Author the hybrid-signed bundle with ONE fluent call.
        // ──────────────────────────────────────────────────────────────
        var bundle = new BundleBuilder()
            .Name("Hybrid PQ Signing Demo")
            .Manufacturer("FalkForge Demo")
            .Version("1.0.0")
            .BundleId(new Guid("893F153E-D38A-497D-AE79-3FC012400794"))
            .UpgradeCode(new Guid("D5ADA931-096F-40AE-A51F-983110CE50D9"))
            .Scope(InstallScope.PerMachine)
            .Integrity(i => i.HybridKey(classicalPem, pqPem))
            .Chain(chain => chain
                .MsiPackage(msiPath, msi => msi
                    .Id("HybridPqSigningDemoApp")
                    .DisplayName("Hybrid PQ Signing Demo Application")
                    .Version("1.0.0")
                    .Vital(true)))
            .Build();

        var buildResult = new BundleCompiler().Compile(bundle, outputPath);
        if (!buildResult.IsSuccess)
            return buildResult;

        Console.WriteLine($"Hybrid-signed bundle compiled: {buildResult.Value}");
        Console.WriteLine();

        // ──────────────────────────────────────────────────────────────
        // 3. Read the envelope back: two entries, classical first.
        // ──────────────────────────────────────────────────────────────
        var envelopeResult = ReadEnvelope(buildResult.Value);
        if (!envelopeResult.IsSuccess)
            return Result<string>.Failure(envelopeResult.Error);
        var envelope = envelopeResult.Value;

        Console.WriteLine($"Envelope carries {envelope.Signatures.Count} signature entries:");
        foreach (var entry in envelope.Signatures)
        {
            // An absent algorithm field IS ECDSA-P256 on the wire (pre-hybrid compatibility).
            var algorithm = entry.Algorithm ?? SignatureAlgorithms.EcdsaP256;
            Console.WriteLine($"  [{algorithm}] fingerprint {entry.Fingerprint}");
        }
        Console.WriteLine();

        // ──────────────────────────────────────────────────────────────
        // 4. Verify with the REAL envelope verifier, companion-pinned:
        //    the classical entry must verify AND the pinned ML-DSA
        //    companion must verify over the same signed bytes. This is
        //    exactly the check the engine's trust gates run; a shipped
        //    engine pins the pair via EngineTrustAnchor.TrustHybridKey
        //    or baked PqFingerprint= metadata (see README).
        // ──────────────────────────────────────────────────────────────
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { classicalFingerprint };
        var pqPolicy = new PqCompanionPolicy
        {
            Companions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [classicalFingerprint] = pqFingerprint
            }
        };

        var hybridVerify = IntegrityEnvelopeCodec.MatchTrustedSignature(
            envelope, trusted, revokedFingerprints: null, pqPolicy);
        Console.WriteLine(hybridVerify.IsSuccess
            ? $"Hybrid verification PASSED (accepted classical fingerprint {hybridVerify.Value})."
            : $"Hybrid verification FAILED unexpectedly: {hybridVerify.Error.Message}");
        Console.WriteLine();

        // ──────────────────────────────────────────────────────────────
        // 5. The strip attack: drop the ML-DSA entry, keep the (still
        //    perfectly valid!) classical signature. The pinned companion
        //    cannot be satisfied -> INT011. This is the anti-strip
        //    guarantee -- quantum-breaking ECDSA alone is not enough.
        // ──────────────────────────────────────────────────────────────
        var strippedEnvelope = IntegrityEnvelopeCodec.Parse(IntegrityEnvelopeCodec.Serialize(envelope))!;
        strippedEnvelope.Signatures = envelope.Signatures
            .Where(e => e.Algorithm is null)
            .ToList();

        var strippedVerify = IntegrityEnvelopeCodec.MatchTrustedSignature(
            strippedEnvelope, trusted, revokedFingerprints: null, pqPolicy);
        Console.WriteLine(strippedVerify.IsFailure
            ? $"Stripped-PQ verification correctly REJECTED: {strippedVerify.Error.Message}"
            : "Stripped-PQ verification unexpectedly passed -- the anti-strip guarantee is broken!");

        return buildResult;
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
});

// Builds a minimal MSI (the same shape as the sibling msi-package/ project) into
// tempDir so this demo is a single, self-contained `dotnet run` with no external
// dependency and no required prior step.
static Result<string> BuildPayloadMsi(string tempDir)
{
    var payloadPath = Path.Combine(tempDir, "app.exe");
    File.WriteAllBytes(payloadPath, []);

    var builder = new PackageBuilder
    {
        Name = "Hybrid PQ Signing Demo Application",
        Manufacturer = "FalkForge Demo",
        Version = new Version(1, 0, 0),
        UpgradeCode = new Guid("7D0762A5-998A-4E44-9A44-6DE357833D50")
    };
    builder.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "HybridPqSigningDemo";
    builder.DefaultInstallDirectory = installDir;
    builder.Files(f => f.Add(payloadPath).To(installDir));
    builder.MajorUpgrade(_ => { });
    builder.Downgrade(d => d.Block("A newer version is already installed."));

    var package = builder.Build();
    var msiDir = Path.Combine(tempDir, "msi");
    Directory.CreateDirectory(msiDir);
    return new MsiCompiler().Compile(package, msiDir);
}

// Extracts and parses the manifest signature envelope from a compiled bundle --
// the same bytes the engine reads before extracting any payload.
static Result<ManifestSignatureEnvelope> ReadEnvelope(string bundlePath)
{
    var content = PayloadEmbedder.Extract(bundlePath);
    if (!content.IsSuccess)
        return Result<ManifestSignatureEnvelope>.Failure(content.Error);

    var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!);
    if (manifest?.ManifestSignature is null)
        return Result<ManifestSignatureEnvelope>.Failure(ErrorKind.SecurityError,
            "The compiled bundle's manifest carries no signature envelope.");

    var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
    if (envelope is null)
        return Result<ManifestSignatureEnvelope>.Failure(ErrorKind.SecurityError,
            "The compiled bundle's manifest signature envelope could not be parsed.");

    return Result<ManifestSignatureEnvelope>.Success(envelope);
}
