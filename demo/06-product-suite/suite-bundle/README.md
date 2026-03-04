# Demo 06: Product Suite -- Suite Bundle

The EXE bootstrapper that wraps the application and service MSI packages into a single installable bundle. Demonstrates the `BundleBuilder` and `BundleCompiler` workflow with rollback boundaries, built-in UI, and chained MSI packages.

## What This Demonstrates

- `Installer.BuildBundle` entry point for bundle creation
- `BundleBuilder` fluent API for defining bundle metadata, scope, and package chain
- `BundleCompiler` for compiling the bundle model into a self-extracting EXE
- Rollback boundaries (`RollbackBoundary`) to isolate package failures
- Chaining multiple MSI packages with `MsiPackage()` including Id, DisplayName, and Vital flag
- Built-in bundle UI with license file and theme color via `UseBuiltInUI()`

## Key API Calls

```csharp
Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Acme Product Suite")
        .Manufacturer("Acme Corporation")
        .Version("2.0.0")
        .BundleId(new Guid("..."))
        .UpgradeCode(new Guid("..."))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI(licenseFile: licenseFile, themeColor: "#2563EB")
        .Chain(chain => chain
            .RollbackBoundary("Prerequisites")
            .MsiPackage(appMsiPath, p => p
                .Id("AcmeApp")
                .DisplayName("Acme Application")
                .Vital(true))
            .RollbackBoundary("Services")
            .MsiPackage(serviceMsiPath, p => p
                .Id("AcmeService")
                .DisplayName("Acme Background Service")
                .Vital(true)))
        .Build();

    var compiler = new BundleCompiler();
    return compiler.Compile(bundle, outputPath);
});
```

## How to Build

Build the MSI packages first, then the bundle:

```
dotnet build demo/06-product-suite/app-installer/app-installer.csproj
dotnet build demo/06-product-suite/service-installer/service-installer.csproj
dotnet build demo/06-product-suite/suite-bundle/suite-bundle.csproj
```

## Notes

- `Vital(true)` means the bundle will fail and trigger rollback if that package fails to install.
- Rollback boundaries define isolation groups. If a package fails after a boundary, only packages within that boundary's group are rolled back.
- The bundle references MSI outputs by relative path (`../app-installer/app-installer.msi`). In production, use the FalkForge SDK source generator (`ProjectOutputs.AppInstaller`) for compile-safe references.
