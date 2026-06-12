# Demo 15: Bundle Signing

A two-project demo showing the complete code-signing workflow for FalkForge bundle EXEs. The process involves compiling a bundle, detaching the PE stub from the payload data, signing the stub with Authenticode, and reattaching it -- preserving all payload offsets.

## What This Demonstrates

- The detach/sign/reattach workflow required for Authenticode signing of bundle EXEs
- `BundleDetacher.Detach` to split a compiled bundle into a bare PE stub and a data file
- `BundleDetacher.Reattach` to combine a signed stub with the data file into the final bundle
- Automatic TOC patching to account for stub size changes introduced by the Authenticode signature
- `Result<T>` error handling pattern throughout the compile/detach/reattach pipeline

## Project Structure

| Sub-project | Type | Description |
|---|---|---|
| `msi-package/` | MSI | Minimal MSI package used as a chain item by the bundle |
| `bundle/` | Bundle (EXE) | Compiles the bundle and performs the detach/sign/reattach workflow |

## How to Build

Build the MSI package first, then the bundle:

```
dotnet build demo/15-bundle-signing/msi-package/msi-package.csproj
dotnet build demo/15-bundle-signing/bundle/bundle.csproj
```

## Payload Integrity Signing (ECDSA) — a separate layer

Authenticode (above) signs the bundle EXE so Windows trusts its publisher. FalkForge
adds a second, independent layer: an **ECDSA signature over the payload hashes**,
embedded in the bundle manifest and verified by the engine *before any payload runs*.
This detects tampering with the payloads even when the attacker also rewrites the
manifest hashes — they cannot forge the signature without the private key. It works
regardless of Authenticode and needs no external tool at install time.

Enable it with the fluent `Integrity(...)` API on the bundle:

```csharp
var model = new BundleBuilder()
    .Name("MyApp")
    // ...
    .Integrity(i => { })                       // ephemeral P-256 key: zero-config tamper detection
    // or: .Integrity(i => i.SigningKey("signing.pem"))  // stable key for authorship proof
    .Chain(chain => chain.MsiPackage(msiPath, p => p.Id("App")))
    .Build();
```

- **Ephemeral key (default):** a throwaway P-256 key is generated per build. Every build
  has a unique key, so detection is zero-config and a compromised key affects only one build.
- **Configured PEM key (`SigningKey(path)`):** the same public key is embedded across builds,
  giving authorship proof. A missing key file fails the build with `SGN002`.
- **Pure .NET:** signing uses `System.Security.Cryptography.ECDsa` — no `sigil` CLI required.
  (When `sigil` is on PATH, a supplementary DSSE SBOM attestation is also produced; it is
  optional provenance and never blocks the signature.)
- **Reproducible builds:** the signed content (payload hashes) is identical across
  reproducible builds; only the ECDSA signature bytes — a deliberately non-deterministic,
  post-content addition — differ. The signature lives outside the reproducible content digest.
- **Runtime:** the engine verifies the signature and binds each signed hash to its payload
  before the Apply phase. A mismatch aborts the install with a `SecurityError`; an unsigned
  bundle installs normally (backward compatible).

## Notes

- The signing step is a placeholder in this demo. In a real CI/CD pipeline, insert your `signtool` or signing service call between the detach and reattach steps.
- The detach/reattach process is necessary because Authenticode signs the PE headers and sections. Bundle payloads are appended after the PE image, so the stub must be signed before the payload is attached.
- Intermediate files (`.stub.exe` and `.data`) are cleaned up after reattach. In a build pipeline, you may want to keep them for debugging.
- The final signed output is written to a separate file (`.signed.exe`) to preserve the original unsigned bundle for comparison.
