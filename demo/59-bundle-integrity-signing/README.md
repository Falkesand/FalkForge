# Demo 59: Bundle Integrity Signing

A self-contained demo of the ECDSA payload-integrity layer that demo 15's README described
but never actually exercised: `BundleBuilder.Integrity(...)`, the independent signature over
a bundle's payload hashes that the engine verifies before any payload runs.

## What This Demonstrates

- `BundleBuilder.Integrity(...)` with an **ephemeral key** (`.Integrity(i => { })`) --
  zero-config, a throwaway P-256 key generated for the one build
- `BundleBuilder.Integrity(...)` with a **stable PEM key** (`.Integrity(i => i.SigningKey(path))`)
  -- the same public key embedded across builds, giving authorship proof
- Reading the compiled bundle's manifest back and verifying the embedded signature with
  `IntegrityEnvelopeCodec`, exactly as the engine does before extracting any payload
- That signing is orthogonal to Authenticode: it needs no external tool (`sigil`, `signtool`)
  and protects the payload hashes even if an attacker rewrites the bundle's own (unsigned)
  table of contents

## Project Structure

| Sub-project | Type | Description |
|---|---|---|
| `msi-package/` | MSI | The same minimal MSI package shape as demo 15's chain item; buildable and runnable on its own |
| `bundle/` | Bundle (EXE) | Builds its own copy of that MSI inline into a temp directory, then compiles two signed bundles (ephemeral key, stable key) |

`bundle/Program.cs` does **not** depend on `msi-package/` having been run first -- it builds
a minimal MSI in-process (the same builder calls as `msi-package/Program.cs`) so `dotnet run
--project bundle` is a single, self-contained command with no external dependency. The sibling
`msi-package/` project is kept as a standalone example you can build and run independently.

## How to Run

```
dotnet run --project demo/59-bundle-integrity-signing/bundle -- -o ./out
```

This produces two signed bundles in `./out`:

- `Integrity Signing Demo (Ephemeral Key).exe`
- `Integrity Signing Demo (Stable Key).exe` (written to `./out.stable-key.exe/`)

and prints, for each, whether the manifest carries a signature, whether it verifies, and the
signing key's fingerprint.

## Key API Calls

```csharp
var bundle = new BundleBuilder()
    .Name("MyApp")
    // ...
    .Integrity(i => { })                              // ephemeral P-256 key: zero-config tamper detection
    // or: .Integrity(i => i.SigningKey("signing.pem"))  // stable key for authorship proof
    .Chain(chain => chain.MsiPackage(msiPath, p => p.Id("App")))
    .Build();
```

## The Failure Mode (not exercised by this demo)

`SigningKey(path)` fails the build loud with `SGN002` when the key file does not exist --
a publisher who typos the path (or forgets to provision the key in CI) gets a build failure,
never a silently-unsigned bundle. This demo only exercises the success path so it never fails
the build; to see it yourself, point `SigningKey` at a nonexistent path.

## Notes

- **Ephemeral key (default):** a throwaway P-256 key is generated per build. Every build has
  a unique key, so detection is zero-config and a compromised key affects only one build.
- **Configured PEM key (`SigningKey(path)`):** the same public key is embedded across builds,
  giving authorship proof.
- **Pure .NET:** signing uses `System.Security.Cryptography.ECDsa` -- no `sigil` CLI required.
- **Reproducible builds:** the signed content (payload hashes) is identical across
  reproducible builds; only the ECDSA signature bytes -- a deliberately non-deterministic,
  post-content addition -- differ.
- **Runtime:** before any payload is extracted or executed, the engine verifies the signature
  and binds it to the hash in the bundle's unsigned table of contents (TOC). A tampered
  payload whose TOC hash was rewritten after signing is rejected; any signature mismatch
  aborts with a security error. An unsigned bundle installs normally (backward compatible).
- For **trusted-key pinning** (which fingerprints the engine actually trusts, not just which
  signature verifies against its own embedded key) and **key rotation**, see demo 60. For
  **remote signing** (the private key never leaves a signing server), see demo 61. For the
  **runtime update-trust** enforcement this signature feeds into, see demo 62.
