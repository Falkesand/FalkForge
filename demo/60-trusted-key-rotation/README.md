# Demo 60: Trusted Key Rotation

A self-contained demo of the rotation-safe dual-sign workflow and the trusted-key model it
feeds: `IntegrityBuilder.AddSigningKey`/`SigningKeys` sign a bundle's manifest with more than
one key at once, so a bundle can be verified during a key rotation by an engine that only
knows the old fingerprint AND by one that already knows the new one.

## What This Demonstrates

- `BundleBuilder.Integrity(i => i.SigningKeys(oldKeyPath, newKeyPath))` -- dual-signing a
  bundle with two independent P-256 keys
- Reading back the compiled manifest and proving, with `IntegrityEnvelopeCodec.VerifyTrusted`,
  that an engine trusting **only the old fingerprint** accepts the bundle, an engine trusting
  **only the new fingerprint** also accepts it, and an engine trusting **neither** rejects it
- This is the mechanism, in miniature, that makes key rotation safe in production: ship
  dual-signed releases during the overlap window until every deployed engine has observed the
  new key, then drop the old one from future builds

## Project Structure

| Sub-project | Type | Description |
|---|---|---|
| `msi-package/` | MSI | Minimal MSI chain item, buildable/runnable standalone |
| `bundle/` | Bundle (EXE) | Builds its own copy of that MSI inline, then dual-signs a bundle |

`bundle/Program.cs` builds its own payload MSI in-process (see demo 59) so `dotnet run
--project bundle` is a single, self-contained command with no external dependency.

## How to Run

```
dotnet run --project demo/60-trusted-key-rotation/bundle -- -o ./out
```

## Key API Calls

```csharp
var bundle = new BundleBuilder()
    .Name("MyApp")
    // ...
    .Integrity(i => i.SigningKeys("old-release-key.pem", "new-release-key.pem"))
    .Chain(chain => chain.MsiPackage(msiPath, p => p.Id("App")))
    .Build();
```

`AddSigningKey(path)` adds one key at a time (repeatable); `SigningKeys(params)` adds several
at once. Every added key signs the identical canonical message and contributes one entry to
the manifest's signature list.

## Beyond This Demo: Trusted-Key Pinning, Roles, and Quorum

`IntegrityEnvelopeCodec.VerifyTrusted` (used above) proves a signature is valid against a
*caller-supplied* trusted set. The engine itself does not read that set from the bundle or
from a config file next to the EXE -- it is pinned at build time into the engine binary,
outside anything the bundle carries.

### Baked trust: `-p:FalkForgeTrustedKey`

A publisher builds their own `FalkForge.Engine.exe` with their own trusted fingerprints:

```
dotnet build ... -p:FalkForgeTrustedKey=6C0C66E36EFE54BF5796C2D5DE2D9A402CAF8B2CFAF590769BA46DE784A98AE1
```

(repeatable via multiple `<FalkForgeTrustedKey Include="..."/>` items in a project file). Each
fingerprint is the SHA-256 of a signing key's SubjectPublicKeyInfo, hex -- exactly the value
this demo prints for each dual-signed entry. The generated `FrozenSet` is baked into the
engine binary at compile time (`FalkForge.Engine/TrustedKeys.targets`) and is the *only* trust
anchor the runtime honors.

### Code-side registration: `EngineTrustAnchor`

A publisher who rebuilds the engine can also register trusted keys from their own compiled
bootstrap code, before the first verification runs:

```csharp
// In the engine's Program.ConfigureTrust hook -- NEVER reachable from a bundle/manifest/network input.
EngineTrustAnchor.TrustFingerprint("6C0C66E3...", TrustRole.Release);
```

This is additive to the baked set, never a replacement, and the effective set freezes
permanently on first read -- registering after the first verification throws.

### Key roles and quorum (C19)

A trusted key is no longer just "trusted" or not -- it is tagged with one or more
`TrustRole` values (`Release`, `Recovery`, `Security`, `EmergencyRevoke`, `Ci`, `Developer`,
`Timestamp`). The baked default policy (`BakedTrustPolicy`) requires different role
combinations for different operations:

| Operation | Requires |
|---|---|
| Install / Update | 1 `Release` signature |
| Key change (rotation) | 1 `Release` **AND** 1 `Recovery` signature (2 distinct keys) |
| Downgrade | 1 `Release` **AND** 1 `Security` signature |
| Revoke | 1 `Release` **AND** (1 `Security` **OR** 1 `EmergencyRevoke`) |

So a real production rotation is stronger than this demo's plain dual-sign: the *routine*
install/update path still accepts one release signature (exactly what this demo shows), but
an actual key-change operation additionally requires a co-signature from a separate
`Recovery`-role key held in different custody -- a compromised online release key alone can
never re-anchor trust. Tag a key's role either via `-p:FalkForgeTrustedKey` item metadata
(`Roles="Release;Recovery"`) or `EngineTrustAnchor.TrustFingerprint(fp, TrustRole.Release |
TrustRole.Recovery)`. An un-roled trusted key defaults to `Release`, so an un-migrated engine
keeps exactly the flat "any trusted key verifies" behavior this demo exercises.

## Notes

- This demo signs and reads the manifest at build time only; it does not build or run an
  actual engine, so the role/quorum table above is narrated, not executed.
- For the single-key ephemeral/stable-key signing basics this demo builds on, see demo 59.
- For remote signing (the private key never touches the build machine), see demo 61.
