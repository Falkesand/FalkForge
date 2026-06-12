using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Manifest;

// Configure automatic update checking from a feed URL.
//
// With UpdatePolicy.DownloadAndPrompt the engine, at startup, fetches the feed, downloads a
// newer bundle in the background (delta-first with SHA-256 verification), and reports progress
// and an "update ready" signal to the UI. The user can then choose to install the update, at
// which point the engine launches the downloaded bundle and shuts itself down for a clean
// handoff.
//
// PinUpdatePublisher pins the Authenticode certificate thumbprint that the downloaded update
// bundle must present. The engine verifies the signature against this thumbprint before
// launching — a mismatch (e.g. a substituted or tampered bundle) is refused as a security error.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Auto-Update Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("33445566-7788-4990-AABB-CCDDEEFF0011"))
        .UpgradeCode(new Guid("44556677-8899-4AA0-BBCC-DDEEFF001122"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .UpdateFeed("https://updates.example.com/myapp/feed.json", UpdatePolicy.DownloadAndPrompt)
        // Pin the publisher's code-signing certificate thumbprint (SHA-1, 40 hex characters).
        // Replace with your real signing certificate thumbprint.
        .PinUpdatePublisher("A1B2C3D4E5F60718293A4B5C6D7E8F9011223344")
        .Chain(chain => chain
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});