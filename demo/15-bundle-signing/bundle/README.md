# Demo 15: Bundle Signing -- Bundle

The bundle project that compiles a self-extracting EXE from the MSI package, then performs the detach/sign/reattach
workflow to produce a signed bundle.

## What This Demonstrates

- `Installer.BuildBundle` entry point for bundle creation
- `BundleBuilder` fluent API for defining a minimal bundle with one MSI package
- `BundleCompiler.Compile` to produce the initial unsigned bundle EXE
- `BundleDetacher.Detach` to split the bundle into PE stub + data
- `BundleDetacher.Reattach` to combine signed stub + data into the final bundle
- `Result<T>` error propagation with `IsSuccess` checks at each step

## Key API Calls

```csharp
Installer.BuildBundle(args, outputPath =>
{
    // 1. Build the bundle model
    var bundle = new BundleBuilder()
        .Name("Signing Demo Bundle")
        .Manufacturer("FalkForge Demo")
        .Version("1.0.0")
        .Chain(chain => chain
            .MsiPackage(msiPath, msi => msi
                .Id("SigningDemoApp")
                .Vital(true)))
        .Build();

    // 2. Compile to EXE
    var compiler = new BundleCompiler();
    var compileResult = compiler.Compile(bundle, outputPath);

    // 3. Detach PE stub from payload data
    BundleDetacher.Detach(outputPath, stubPath, dataPath);

    // 4. Sign the stub (insert signtool call here)

    // 5. Reattach signed stub + data
    BundleDetacher.Reattach(stubPath, dataPath, signedOutputPath);
});
```

## How to Build

Build the MSI package first:

```
dotnet build demo/15-bundle-signing/msi-package/msi-package.csproj
dotnet build demo/15-bundle-signing/bundle/bundle.csproj
```
