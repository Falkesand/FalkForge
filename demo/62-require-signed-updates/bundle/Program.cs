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

// Demo 62 -- Require-Signed Updates
//
// Demonstrates the AUTHORING side of the update-trust config: `.Integrity(...)` with a
// key-epoch and a declared revocation, plus `.UpdateFeed(...)` to point the engine at
// an update feed. Verifying and enforcing that config is a RUNTIME/engine concern --
// the already-installed engine checks a downloaded update against its own baked trust
// before relaunching it, never trusting the downloaded artifact's own embedded engine
// to police itself (see README). This demo builds the authoring side for real and
// narrates the runtime enforcement it feeds.

return Installer.BuildBundle(args, outputPath =>
{
    var tempDir = Directory.CreateTempSubdirectory("falk-demo62-").FullName;
    try
    {
        var msiResult = BuildPayloadMsi(tempDir);
        if (!msiResult.IsSuccess)
            return Result<string>.Failure(msiResult.Error);
        var msiPath = msiResult.Value;

        // A prior publisher key, now retired -- its fingerprint is declared revoked below.
        // Computed the same way the engine derives one (SHA-256 of the SubjectPublicKeyInfo,
        // uppercase hex) so it is a realistic-looking fingerprint, not a placeholder string.
        string revokedFingerprint;
        using (var retiredKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            revokedFingerprint = Convert.ToHexString(SHA256.HashData(retiredKey.ExportSubjectPublicKeyInfo()));

        const int epoch = 2;

        var bundle = new BundleBuilder()
            .Name("Require-Signed Updates Demo")
            .Manufacturer("FalkForge Demo")
            .Version("2.0.0")
            .BundleId(new Guid("2065498B-D5A7-486A-8E5F-BE6A5512DF90"))
            .UpgradeCode(new Guid("DD95523D-BF82-4C7B-8224-F26890D4D096"))
            .Scope(InstallScope.PerMachine)
            .Integrity(i => i
                .Epoch(epoch) // bumped because the retired key below was rotated out
                .Revoke(revokedFingerprint))
            .UpdateFeed("https://updates.example.com/require-signed-updates-demo/feed.json", UpdatePolicy.AutoUpdate)
            .Chain(chain => chain
                .MsiPackage(msiPath, msi => msi
                    .Id("RequireSignedUpdatesDemoApp")
                    .DisplayName("Require-Signed Updates Demo Application")
                    .Version("2.0.0")
                    .Vital(true)))
            .Build();

        var result = new BundleCompiler().Compile(bundle, outputPath);
        if (!result.IsSuccess)
            return result;

        Console.WriteLine($"Bundle compiled: {result.Value}");
        Console.WriteLine($"  key epoch: {epoch}");
        Console.WriteLine($"  revoked fingerprint declared: {revokedFingerprint}");
        Console.WriteLine("  update feed: https://updates.example.com/require-signed-updates-demo/feed.json (AutoUpdate)");
        PrintManifestConfig(result.Value);

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
        Name = "Require-Signed Updates Demo Application",
        Manufacturer = "FalkForge Demo",
        Version = new Version(2, 0, 0),
        UpgradeCode = new Guid("9139AA76-737C-4AC3-B7AF-480C4C0AAF4C")
    };
    builder.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "RequireSignedUpdatesDemo";
    builder.DefaultInstallDirectory = installDir;
    builder.Files(f => f.Add(payloadPath).To(installDir));
    builder.MajorUpgrade(_ => { });
    builder.Downgrade(d => d.Block("A newer version is already installed."));

    var package = builder.Build();
    var msiDir = Path.Combine(tempDir, "msi");
    Directory.CreateDirectory(msiDir);
    return new MsiCompiler().Compile(package, msiDir);
}

// Extracts the manifest and prints the epoch + revocation list actually embedded in the
// signed envelope, plus the update feed config carried on the manifest -- proof that the
// authoring-side config above really landed in the shipped artifact.
static void PrintManifestConfig(string bundlePath)
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

    Console.WriteLine($"  manifest envelope epoch: {envelope.Epoch}");
    Console.WriteLine($"  manifest envelope revoked count: {envelope.Revoked.Count}");
    Console.WriteLine($"  manifest signature verifies: {IntegrityEnvelopeCodec.VerifySignature(envelope)}");
    Console.WriteLine($"  manifest update feed: {manifest.UpdateFeed?.FeedUrl} (policy={manifest.UpdateFeed?.Policy})");
}
