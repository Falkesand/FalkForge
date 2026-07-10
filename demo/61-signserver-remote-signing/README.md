# Demo 61: SignServer Remote Signing

A self-contained demo of wiring a bundle's ECDSA integrity signature to a remote signing
backend -- `SignServerSignatureProvider` -- so the private key never touches the build
machine.

## What This Demonstrates

- `SignServerConfig.FromEnvironment()` -- the SDK's own environment-variable convention for
  configuring a remote signer without secrets in source (`SIGNSERVER_URL`,
  `SIGNSERVER_WORKER`, `SIGNSERVER_AUTH`, plus the matching credential variables)
- `IntegrityBuilder.SigningProvider(provider)` -- plugging a custom `ISignatureProvider` into
  the same `.Integrity(...)` fluent config used by demos 59 and 60
- `Installer.BuildBundleAsync` / `BundleCompiler.CompileAsync` -- the async build pipeline a
  genuinely asynchronous provider (one that performs network I/O) must be driven through; the
  synchronous `BuildBundle`/`Compile` fail loud (`SGN010`) on such a provider rather than block
  a thread on network I/O
- A safe, dependency-free fallback: when no SignServer is configured (the default for a plain
  `dotnet run`), the demo signs with a local ephemeral key instead, so it always builds and
  runs standalone

## Why a Demo Can't Require a Live SignServer

A demo that only builds when a specific server is reachable is not runnable in CI or by
someone cloning the repo cold. This demo resolves that by checking
`SignServerConfig.FromEnvironment()` first: if it fails (no `SIGNSERVER_URL`/`SIGNSERVER_WORKER`
set), the demo prints why and falls back to `.Integrity(i => { })` (the same ephemeral-key path
as demo 59). If it succeeds, the demo signs through the real `SignServerSignatureProvider`
against whatever endpoint you configured.

## Project Structure

| Sub-project | Type | Description |
|---|---|---|
| `msi-package/` | MSI | Minimal MSI chain item, buildable/runnable standalone |
| `bundle/` | Bundle (EXE) | Builds its own copy of that MSI inline, then signs remotely or falls back locally |

## How to Run

Without a SignServer (falls back to a local ephemeral key):

```
dotnet run --project demo/61-signserver-remote-signing/bundle -- -o ./out
```

With a real SignServer instance fronting a PlainSigner worker (ECDSA, SHA256withECDSA):

```
set SIGNSERVER_URL=https://your-signserver:8443
set SIGNSERVER_WORKER=YourWorkerName
set SIGNSERVER_AUTH=none
dotnet run --project demo/61-signserver-remote-signing/bundle -- -o ./out
```

`SIGNSERVER_AUTH` also accepts `clientcert` (plus `SIGNSERVER_CLIENT_CERT` /
`SIGNSERVER_CLIENT_CERT_PASSWORD`), `basic` (plus `SIGNSERVER_BASIC_USER` /
`SIGNSERVER_BASIC_PASS`), or `bearer` (plus `SIGNSERVER_BEARER_TOKEN`). See
`SignServerConfig.FromEnvironment()` for the full contract; a missing URL or worker, or
missing credentials for the selected auth mode, fails loud with `SGN024` rather than silently
signing unauthenticated.

## Key API Calls

```csharp
var configResult = SignServerConfig.FromEnvironment();
using var provider = new SignServerSignatureProvider(configResult.Value);

var bundle = new BundleBuilder()
    .Name("MyApp")
    // ...
    .Integrity(i => i.SigningProvider(provider))
    .Chain(chain => chain.MsiPackage(msiPath, p => p.Id("App")))
    .Build();

// Async pipeline -- required for a genuinely asynchronous provider.
var result = await new BundleCompiler().CompileAsync(bundle, outputPath);
```

## Why Remote Signing

- **The private key never leaves the signing server** (or the HSM behind it). The build
  machine only ever receives a signature and the signer's public certificate -- never key
  material.
- **HSM-backed keys** are a natural fit: SignServer's PlainSigner worker can be backed by a
  hardware security module, so the key is not extractable even from the SignServer host.
- **Offline verification is unaffected.** The provider converts SignServer's DER-encoded
  ECDSA signature to the same IEEE P1363 encoding local PEM signing produces, and extracts the
  same SubjectPublicKeyInfo shape, so the engine's verifier is completely backend-agnostic --
  it cannot tell a bundle was signed remotely.
- **Centralized key custody** means a compromised build agent cannot exfiltrate the signing
  key -- only whatever the SignServer's auth policy allows it to request signatures for.

## Notes

- `SignServerSignatureProvider` implements `IDisposable` (it owns an `HttpClient`); this demo
  disposes it after the build.
- For local single-key and stable-key signing, see demo 59. For dual-sign key rotation and the
  trusted-key/roles model this signature feeds into, see demo 60.
