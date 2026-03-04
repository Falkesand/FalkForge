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

## Notes

- The signing step is a placeholder in this demo. In a real CI/CD pipeline, insert your `signtool` or signing service call between the detach and reattach steps.
- The detach/reattach process is necessary because Authenticode signs the PE headers and sections. Bundle payloads are appended after the PE image, so the stub must be signed before the payload is attached.
- Intermediate files (`.stub.exe` and `.data`) are cleaned up after reattach. In a build pipeline, you may want to keep them for debugging.
- The final signed output is written to a separate file (`.signed.exe`) to preserve the original unsigned bundle for comparison.
