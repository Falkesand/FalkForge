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

// Demo 59 -- Bundle Integrity Signing
//
// Demonstrates the ECDSA payload-integrity layer described (but never exercised) by
// demo 15's README: an independent signature over the bundle's payload hashes,
// embedded in the manifest and verified by the engine before any payload runs. This
// is orthogonal to Authenticode -- it needs no external tool and detects tampering
// even if the attacker rewrites the (unsigned) TOC hashes too.
//
// The fluent entry point is `BundleBuilder.Integrity(...)`:
//   - `.Integrity(i => { })`                    -- ephemeral P-256 key, zero-config
//   - `.Integrity(i => i.SigningKey("key.pem"))` -- a stable key, for authorship proof
//
// This demo builds BOTH variants and reads back the produced manifest to prove the
// signature is really there and really verifies.
//
// A missing key file with `SigningKey(path)` fails the build with SGN002 -- see the
// README for that failure mode; this demo only exercises the success path so it
// never fails the build.
//
// The sibling `msi-package/` project is the same standalone MSI chain item demo 15
// uses (build it directly with `dotnet run --project msi-package` to inspect the
// .msi on its own). This project builds its own copy of that MSI inline into a temp
// directory instead of depending on that separate run, so `dotnet run --project
// bundle` works standalone with no prior step and no external dependency.

return Installer.BuildBundle(args, outputPath =>
{
    var tempDir = Directory.CreateTempSubdirectory("falk-demo59-").FullName;
    try
    {
        var msiResult = BuildPayloadMsi(tempDir);
        if (!msiResult.IsSuccess)
            return Result<string>.Failure(msiResult.Error);
        var msiPath = msiResult.Value;

        // ──────────────────────────────────────────────────────────────
        // 1. Ephemeral key -- zero-config tamper detection
        // ──────────────────────────────────────────────────────────────
        var ephemeralBundle = new BundleBuilder()
            .Name("Integrity Signing Demo (Ephemeral Key)")
            .Manufacturer("FalkForge Demo")
            .Version("1.0.0")
            .BundleId(new Guid("10B178C7-8C24-4020-A712-E5A0CED80010"))
            .UpgradeCode(new Guid("C1C7C342-998D-458E-9317-D1AF17597D80"))
            .Scope(InstallScope.PerMachine)
            .Integrity(i => { }) // ephemeral P-256 key: a throwaway key generated for this build only
            .Chain(chain => chain
                .MsiPackage(msiPath, msi => msi
                    .Id("IntegritySigningDemoApp")
                    .DisplayName("Integrity Signing Demo Application")
                    .Version("1.0.0")
                    .Vital(true)))
            .Build();

        var ephemeralResult = new BundleCompiler().Compile(ephemeralBundle, outputPath);
        if (!ephemeralResult.IsSuccess)
            return ephemeralResult;

        Console.WriteLine($"Ephemeral-key bundle compiled: {ephemeralResult.Value}");
        PrintSignatureConfirmation(ephemeralResult.Value, "ephemeral");

        // ──────────────────────────────────────────────────────────────
        // 2. Stable PEM key -- authorship proof across builds
        // ──────────────────────────────────────────────────────────────
        var pemPath = Path.Combine(tempDir, "integrity-signing-key.pem");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());

        var stableOutputPath = Path.ChangeExtension(outputPath, ".stable-key.exe");
        var stableBundle = new BundleBuilder()
            .Name("Integrity Signing Demo (Stable Key)")
            .Manufacturer("FalkForge Demo")
            .Version("1.0.0")
            .BundleId(new Guid("10B178C7-8C24-4020-A712-E5A0CED80010"))
            .UpgradeCode(new Guid("C1C7C342-998D-458E-9317-D1AF17597D80"))
            .Scope(InstallScope.PerMachine)
            .Integrity(i => i.SigningKey(pemPath)) // same public key embedded across builds
            .Chain(chain => chain
                .MsiPackage(msiPath, msi => msi
                    .Id("IntegritySigningDemoApp")
                    .DisplayName("Integrity Signing Demo Application")
                    .Version("1.0.0")
                    .Vital(true)))
            .Build();

        var stableResult = new BundleCompiler().Compile(stableBundle, stableOutputPath);
        if (!stableResult.IsSuccess)
            return stableResult;

        Console.WriteLine($"Stable-key bundle compiled: {stableResult.Value}");
        PrintSignatureConfirmation(stableResult.Value, "stable PEM");

        return ephemeralResult;
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
        Name = "Integrity Signing Demo Application",
        Manufacturer = "FalkForge Demo",
        Version = new Version(1, 0, 0),
        UpgradeCode = new Guid("142A6BF1-B0F3-4ED4-B938-910C6BA51F59")
    };
    builder.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "IntegritySigningDemo";
    builder.DefaultInstallDirectory = installDir;
    builder.Files(f => f.Add(payloadPath).To(installDir));
    builder.MajorUpgrade(_ => { });
    builder.Downgrade(d => d.Block("A newer version is already installed."));

    var package = builder.Build();
    var msiDir = Path.Combine(tempDir, "msi");
    Directory.CreateDirectory(msiDir);
    return new MsiCompiler().Compile(package, msiDir);
}

// Extracts the manifest from a compiled bundle and confirms the ECDSA signature is
// present and verifies -- exactly what the engine does before extracting any payload.
static void PrintSignatureConfirmation(string bundlePath, string label)
{
    var content = PayloadEmbedder.Extract(bundlePath);
    if (!content.IsSuccess)
    {
        Console.WriteLine($"  ({label}) could not extract manifest: {content.Error.Message}");
        return;
    }

    var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!);
    if (manifest?.ManifestSignature is null)
    {
        Console.WriteLine($"  ({label}) manifest has no signature.");
        return;
    }

    var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
    var verifies = envelope is not null && IntegrityEnvelopeCodec.VerifySignature(envelope);

    Console.WriteLine($"  ({label}) manifest signature present: {envelope is not null}, verifies: {verifies}");
    if (envelope is not null && envelope.Signatures.Count > 0)
        Console.WriteLine($"  ({label}) signing key fingerprint: {envelope.Signatures[0].Fingerprint}");
}
