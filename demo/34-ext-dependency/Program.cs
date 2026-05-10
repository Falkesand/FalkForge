using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Extensions.Dependency;

// Register as a dependency provider and declare a dependency requirement.
var dependency = new DependencyExtension();

// This package provides "Demo.SharedRuntime" version 1.0.0
dependency.Provides("Demo.SharedRuntime", provider => provider
    .Version("1.0.0")
    .DisplayName("Demo Shared Runtime"));

// This package requires "Demo.Framework" >= 2.0.0
dependency.Requires("Demo.Framework", consumer => consumer
    .ConsumerKey("Demo.App")
    .MinVersion("2.0.0")
    .MinInclusive());

Console.WriteLine("Dependency: 1 provider, 1 consumer configured. Validation runs automatically during compilation.");

// In production, extensions register automatically via the FalkForge SDK extension
// pipeline during compilation. The package below shows the MSI structure; extension
// tables are emitted by the SDK at build time.
return Installer.Build(args, package =>
{
    package.Name = "Dependency Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "DependencyDemo"));
}, new MsiCompiler());