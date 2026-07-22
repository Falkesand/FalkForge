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

## Which Signing Path Should You Use?

There are now two ways to get an Authenticode signature onto a bundle EXE. They protect the same
thing (publisher identity, not payload tampering — that's the separate ECDSA layer above) but differ
in *when* and *how* the file is signed.

### Recommended: sign the fully assembled bundle directly

Once `installer.exe` is completely compiled — stub + FALKBUNDLE container (magic, manifest, payloads,
TOC, footer) all embedded — hand that single file straight to `signtool`. No detach step at all:

```
signtool sign /fd sha256 /f mycert.pfx /p <password> installer.exe
```

The Authenticode signature then legitimately covers the **entire file**, stub and container both,
with the certificate table appended after the genuine end of the pre-signing file. This is not the
CVE-2013-3900 padding trick described below — it is exactly how Authenticode is meant to work, so it
passes strict certificate-padding validation on any machine, including ones with the opt-in hardening
enabled.

The only thing this requires on FalkForge's side: `BundleReader` must still find the FALKBUNDLE footer
even though the appended certificate table now sits after it (physical EOF is no longer the footer's
last byte). It does — `BundleReader`'s footer lookup falls back to the PE optional header's Security
data directory when the footer isn't at the file's physical end, tries the small (0–7 byte) alignment
padding window signtool may insert ahead of the certificate table, and only trusts a hit whose magic
bytes actually match. A whole-bundle-signed EXE self-extracts exactly like an unsigned one.

Use this path whenever it fits your pipeline: you have the whole compiled file and a local or
CI-reachable signing tool (or HSM/token) at that point. It is simpler than detach/reattach — one
`signtool` call, no intermediate files, no TOC offset patching — and it sidesteps the CVE-2013-3900
caveat entirely.

### Fallback: detach / sign / reattach

Use detach/reattach when the signing step genuinely cannot see the fully assembled file — for example
a remote signing service that is only ever handed a bare PE stub (never your payloads), or a pipeline
stage that signs before the payload set for that build is finalized. This demo's code exercises that
workflow.

## Notes

- The signing step is a placeholder in this demo. In a real CI/CD pipeline, insert your `signtool` or signing service call between the detach and reattach steps.
- The detach/reattach process is necessary (for this fallback path) because Authenticode signs a PE and stores the signature in an attribute-certificate table that must be the LAST bytes of the file. Appending payload data after signing would push that table off the end and Windows would report the whole file as unsigned. `BundleDetacher.Reattach` avoids that: after appending the bundle data it extends the PE Security data-directory (and the trailing certificate entry) to cover the appended bytes, so the certificate table again ends at the file. Those two length fields sit in regions Authenticode excludes from its digest, so the publisher's signature — applied to the bare stub — stays valid over the reattached bundle. The signature is only preserved through this reattach step (a plain append would drop it), and publisher trust still depends on signing with a certificate that chains to a trusted root. Note: this relies on default `WinVerifyTrust` behavior; a machine with the opt-in CVE-2013-3900 certificate-padding hardening enabled treats the trailing bytes inside the enlarged certificate as tampering and would reject the signature. **This is exactly the caveat the "sign the whole assembled bundle" path above avoids** — prefer it unless your pipeline genuinely can't sign the whole file.
- Intermediate files (`.stub.exe` and `.data`) are cleaned up after reattach. In a build pipeline, you may want to keep them for debugging.
- The final signed output is written to a separate file (`.signed.exe`) to preserve the original unsigned bundle for comparison.
