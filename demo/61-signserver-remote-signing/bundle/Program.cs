using System.Text.Json;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Signing.SignServer;

// Demo 61 -- SignServer Remote Signing
//
// Demonstrates wiring a bundle's ECDSA integrity signature to a remote signing
// backend -- SignServerSignatureProvider -- instead of a local PEM file. The private
// key never leaves the SignServer instance (or the HSM behind it); the build machine
// only ever sees the resulting signature and the signer's public certificate.
//
// A demo cannot require a live SignServer, so this one is guarded: it calls the SDK's
// own SignServerConfig.FromEnvironment() (SIGNSERVER_URL, SIGNSERVER_WORKER, ...). When
// that succeeds it signs remotely through the real provider on the async build pipeline
// (Installer.BuildBundleAsync / BundleCompiler.CompileAsync, required for a genuinely
// asynchronous ISignatureProvider). When it fails (no SignServer configured -- the
// default for `dotnet run` with no environment set up) it falls back to a local
// ephemeral key so the demo always builds and runs standalone.
//
// To see the remote path for real, point a SignServer instance at a PlainSigner
// (ECDSA, SHA256withECDSA) worker and set:
//   SIGNSERVER_URL=https://your-signserver:8443
//   SIGNSERVER_WORKER=YourWorkerName
//   SIGNSERVER_AUTH=none|clientcert|basic|bearer   (plus the matching credential vars)

return await Installer.BuildBundleAsync(args, async outputPath =>
{
    var tempDir = Directory.CreateTempSubdirectory("falk-demo61-").FullName;
    try
    {
        var msiResult = BuildPayloadMsi(tempDir);
        if (!msiResult.IsSuccess)
            return Result<string>.Failure(msiResult.Error);
        var msiPath = msiResult.Value;

        var configResult = SignServerConfig.FromEnvironment();

        SignServerSignatureProvider? remoteProvider = null;
        try
        {
            var bundleBuilder = new BundleBuilder()
                .Name("SignServer Remote Signing Demo")
                .Manufacturer("FalkForge Demo")
                .Version("1.0.0")
                .BundleId(new Guid("79CC224E-AF7A-4D19-A47E-978CBC3BE24E"))
                .UpgradeCode(new Guid("D8CC00AF-7613-4DE5-8FB3-FE0FD08CB8C3"))
                .Scope(InstallScope.PerMachine);

            if (configResult.IsSuccess)
            {
                Console.WriteLine($"SignServer configured at '{configResult.Value.BaseUrl}' -- signing remotely.");
                remoteProvider = new SignServerSignatureProvider(configResult.Value);
                bundleBuilder.Integrity(i => i.SigningProvider(remoteProvider));
            }
            else
            {
                Console.WriteLine($"No SignServer configured ({configResult.Error.Message}); " +
                                   "falling back to a local ephemeral key for this demo run. " +
                                   "Set SIGNSERVER_URL/SIGNSERVER_WORKER to sign remotely instead.");
                bundleBuilder.Integrity(i => { }); // ephemeral P-256 key: local fallback only
            }

            var bundle = bundleBuilder
                .Chain(chain => chain
                    .MsiPackage(msiPath, msi => msi
                        .Id("SignServerDemoApp")
                        .DisplayName("SignServer Remote Signing Demo Application")
                        .Version("1.0.0")
                        .Vital(true)))
                .Build();

            var result = await new BundleCompiler().CompileAsync(bundle, outputPath);
            if (!result.IsSuccess)
                return result;

            Console.WriteLine($"Bundle compiled: {result.Value}");
            PrintSignatureConfirmation(result.Value, configResult.IsSuccess ? "SignServer" : "local ephemeral");

            return result;
        }
        finally
        {
            remoteProvider?.Dispose();
        }
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
        Name = "SignServer Remote Signing Demo Application",
        Manufacturer = "FalkForge Demo",
        Version = new Version(1, 0, 0),
        UpgradeCode = new Guid("C6CCD504-4679-48B9-A0A6-886D9D9F9F25")
    };
    builder.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "SignServerDemo";
    builder.DefaultInstallDirectory = installDir;
    builder.Files(f => f.Add(payloadPath).To(installDir));
    builder.MajorUpgrade(_ => { });
    builder.Downgrade(d => d.Block("A newer version is already installed."));

    var package = builder.Build();
    var msiDir = Path.Combine(tempDir, "msi");
    Directory.CreateDirectory(msiDir);
    return new MsiCompiler().Compile(package, msiDir);
}

// Extracts the manifest and confirms the signature -- same shape as demo 59.
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
