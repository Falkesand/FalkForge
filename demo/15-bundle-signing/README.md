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

This demo's code only exercises the Authenticode detach/sign/reattach workflow above; it
does not call `Integrity(...)`. For a runnable demo of the ECDSA layer — `BundleBuilder
.Integrity(...)` with an ephemeral key and a stable PEM key, plus reading the signature
back from the compiled manifest and verifying it — see **demo 59: Bundle Integrity
Signing**. For dual-sign key rotation and the trusted-key/roles model, see demo 60. For
wiring a remote signing backend, see demo 61. For the update-trust config (epoch +
revocation) this signature feeds into, see demo 62.

## Notes

- The signing step is a placeholder in this demo. In a real CI/CD pipeline, insert your `signtool` or signing service call between the detach and reattach steps.
- The detach/reattach process is necessary because Authenticode does not cover data appended to the file after it is signed. The stub is signed bare and the payloads are reattached afterward — the signature stays valid, but the reattached payloads sit past the point it protects.
- Intermediate files (`.stub.exe` and `.data`) are cleaned up after reattach. In a build pipeline, you may want to keep them for debugging.
- The final signed output is written to a separate file (`.signed.exe`) to preserve the original unsigned bundle for comparison.
