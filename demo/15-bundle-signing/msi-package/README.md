# Demo 15: Bundle Signing -- MSI Package

A minimal MSI package that serves as the payload for the parent bundle's signing demonstration. Intentionally kept
simple since the focus of this demo is the detach/sign/reattach workflow.

## What This Demonstrates

- Minimal `Installer.Build` MSI package definition
- Explicit `MsiCompiler` instance passed to `Installer.Build`
- `MsiDialogSet.Minimal` for a non-interactive package meant to be driven by a bundle
- Standard major upgrade and downgrade blocking pattern
- Configuring Authenticode signing with `p.Signing()` -- certificate store, timestamp server, algorithm, and description

## Key API Calls

```csharp
Installer.Build(args, p =>
{
    p.Name = "Signing Demo Application";
    p.Manufacturer = "FalkForge Demo";
    p.Version = new Version(1, 0, 0);
    p.UpgradeCode = new Guid("...");

    p.UseDialogSet(MsiDialogSet.Minimal);

    var installDir = KnownFolder.ProgramFiles / "FalkForge Demo" / "SigningDemo";
    p.DefaultInstallDirectory = installDir;

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .To(installDir));

    p.MajorUpgrade(_ => { });
    p.Downgrade(d => d.Block("A newer version is already installed."));

    p.Signing(s =>
    {
        s.Thumbprint("ABC123DEF456");
        s.Store("My");
        s.Timestamp("http://timestamp.digicert.com");
        s.Algorithm("sha256");
        s.WithDescription("FalkForge Demo Installer", "https://example.com");
    });
}, new MsiCompiler());
```

## How to Build

```
dotnet build demo/15-bundle-signing/msi-package/msi-package.csproj
```

## Notes

- This MSI uses `MsiDialogSet.Minimal` because it is designed to be installed silently by the parent bundle, not run
  standalone.
- The explicit `new MsiCompiler()` parameter demonstrates passing a compiler instance directly, which can be useful for
  customizing compiler options.
- `s.Store("My")` selects the Windows certificate store to search for the signing certificate. "My" is the Personal
  store.
- `s.Timestamp()` points to an RFC 3161 timestamp server so the signature remains valid after the certificate expires.
- `s.Algorithm("sha256")` sets the digest algorithm for the signature. SHA-256 is the current industry standard.
- `s.WithDescription()` embeds a description and info URL into the Authenticode signature, shown by Windows during UAC
  prompts.
