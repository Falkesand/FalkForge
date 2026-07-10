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

// Demo 60 -- Trusted Key Rotation
//
// Demonstrates the rotation-safe dual-sign workflow: `IntegrityBuilder.AddSigningKey`/
// `SigningKeys` sign the identical manifest message with every configured key, so a
// bundle signed with both an OLD and a NEW key is accepted by any engine that trusts
// either fingerprint. That overlap window is what makes key rotation safe -- you ship
// dual-signed releases until every deployed engine has observed the new key, then drop
// the old one.
//
// This project builds its own payload MSI inline (see BuildPayloadMsi below), exactly
// like demo 59, so `dotnet run --project bundle` is a single, self-contained command.
// The sibling `msi-package/` project is kept as a standalone example.

return Installer.BuildBundle(args, outputPath =>
{
    var tempDir = Directory.CreateTempSubdirectory("falk-demo60-").FullName;
    try
    {
        var msiResult = BuildPayloadMsi(tempDir);
        if (!msiResult.IsSuccess)
            return Result<string>.Failure(msiResult.Error);
        var msiPath = msiResult.Value;

        // The "old" (currently trusted) and "new" (incoming) release keys.
        var oldKeyPath = Path.Combine(tempDir, "old-release-key.pem");
        var newKeyPath = Path.Combine(tempDir, "new-release-key.pem");
        using (var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(oldKeyPath, oldKey.ExportPkcs8PrivateKeyPem());
        using (var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(newKeyPath, newKey.ExportPkcs8PrivateKeyPem());

        var bundle = new BundleBuilder()
            .Name("Key Rotation Demo")
            .Manufacturer("FalkForge Demo")
            .Version("1.0.0")
            .BundleId(new Guid("659AD977-8F4C-44F8-B0AA-E873B99C978E"))
            .UpgradeCode(new Guid("374E8BDE-718C-4CA0-AF30-4F093AAAEA01"))
            .Scope(InstallScope.PerMachine)
            // Dual-sign: every added key signs the identical payload message, so this
            // bundle is accepted by an engine trusting the old fingerprint OR the new one.
            .Integrity(i => i.SigningKeys(oldKeyPath, newKeyPath))
            .Chain(chain => chain
                .MsiPackage(msiPath, msi => msi
                    .Id("KeyRotationDemoApp")
                    .DisplayName("Key Rotation Demo Application")
                    .Version("1.0.0")
                    .Vital(true)))
            .Build();

        var result = new BundleCompiler().Compile(bundle, outputPath);
        if (!result.IsSuccess)
            return result;

        Console.WriteLine($"Dual-signed bundle compiled: {result.Value}");
        PrintSignatures(result.Value);

        return result;
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
});

// Builds a minimal MSI (same shape as demo 59) into tempDir.
static Result<string> BuildPayloadMsi(string tempDir)
{
    var payloadPath = Path.Combine(tempDir, "app.exe");
    File.WriteAllBytes(payloadPath, []);

    var builder = new PackageBuilder
    {
        Name = "Key Rotation Demo Application",
        Manufacturer = "FalkForge Demo",
        Version = new Version(1, 0, 0),
        UpgradeCode = new Guid("51CAA378-B6E8-41D7-9354-F3A1B55CDC08")
    };
    builder.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "KeyRotationDemo";
    builder.DefaultInstallDirectory = installDir;
    builder.Files(f => f.Add(payloadPath).To(installDir));
    builder.MajorUpgrade(_ => { });
    builder.Downgrade(d => d.Block("A newer version is already installed."));

    var package = builder.Build();
    var msiDir = Path.Combine(tempDir, "msi");
    Directory.CreateDirectory(msiDir);
    return new MsiCompiler().Compile(package, msiDir);
}

// Extracts the manifest and prints every signature entry -- the rotation overlap in
// concrete form: two independent signatures over the identical file-hash list.
static void PrintSignatures(string bundlePath)
{
    var content = PayloadEmbedder.Extract(bundlePath);
    if (!content.IsSuccess)
    {
        Console.WriteLine($"  could not extract manifest: {content.Error.Message}");
        return;
    }

    var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!);
    if (manifest?.ManifestSignature is null)
    {
        Console.WriteLine("  manifest has no signature.");
        return;
    }

    var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
    if (envelope is null)
    {
        Console.WriteLine("  manifest signature could not be parsed.");
        return;
    }

    Console.WriteLine($"  manifest carries {envelope.Signatures.Count} signature(s):");
    foreach (var sig in envelope.Signatures)
        Console.WriteLine($"    fingerprint={sig.Fingerprint} keyId='{sig.KeyId}'");

    // The rotation overlap, made concrete: an engine that trusts ONLY the old fingerprint
    // still accepts this bundle, and so does one that trusts ONLY the new fingerprint --
    // because VerifyTrusted accepts as soon as ONE signature matches a trusted key.
    foreach (var sig in envelope.Signatures)
    {
        var trustingOnlyThisKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sig.Fingerprint };
        var accepted = IntegrityEnvelopeCodec.VerifyTrusted(envelope, trustingOnlyThisKey).IsSuccess;
        Console.WriteLine($"  an engine trusting ONLY {sig.Fingerprint[..12]}... accepts this bundle: {accepted}");
    }

    var strangerFingerprint = new string('0', 64);
    var trustingNeither = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { strangerFingerprint };
    var rejectedByStranger = IntegrityEnvelopeCodec.VerifyTrusted(envelope, trustingNeither).IsFailure;
    Console.WriteLine($"  an engine trusting neither key rejects this bundle: {rejectedByStranger}");
}
