# Provable Installer — Supply Chain Provenance Reference

FalkForge is the only installer framework that builds a complete provenance surface into
its compilation pipeline. This document is the authoritative reference for every provenance
artifact: where it comes from, where it lands, and how to verify it.

---

## Why Provenance Matters

US Executive Order 14028 and the EU Cyber Resilience Act mandate that software distributors
understand and attest to the origin of every component in a shipped binary. For installer
frameworks, this means knowing:

- **What files are in this installer?** (SBOM)
- **Did this installer compile reproducibly from declared source?** (Reproducible builds)
- **Who signed the payload hashes?** (ECDSA integrity)
- **What packages are on the public registry?** (WinGet manifest)
- **What will this installer do before running it?** (Plan export / dry-run)

FalkForge addresses all five questions without external tooling dependencies.

---

## 1. Reproducible Builds

### What it is

When `.Reproducible()` is enabled on `PackageBuilder`, FalkForge derives a deterministic
`PackageCode` (UUID v5 of the content digest) instead of generating a fresh GUID per build.
Two builds from identical source and files produce byte-for-byte identical MSI output
(same structure, same hashes, same `PackageCode`).

### Why it matters — SECREPAIR

Non-reproducible `PackageCode` values cause SECREPAIR: if two MSI builds have the same
`ProductCode`/`UpgradeCode` but different `PackageCode` values, Windows Installer shows a
"Files in use" or repair prompt during upgrade. This is Windows Installer bug #1 in the
field. Reproducible builds prevent it entirely.

### How it works

`PackageCodeDerivation` (in `Compiler.Msi`) computes a UUID v5 from a SHA-256 digest of
all resolved file hashes concatenated in deterministic order. The same files always produce
the same code.

**Env var integration:** set `SOURCE_DATE_EPOCH` to a Unix timestamp to also pin the MSI's
internal `LastModified` timestamp. FalkForge reads this variable in the SBOM timestamp path
and any date-stamped table cells.

### Fluent API

```csharp
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .Reproducible()   // deterministic PackageCode + file ordering
    .Feature("Main", f => f.File("MyApp.exe")));
```

### Verification

```bash
# Build twice, compare SHA-256 of output
forge build installer.csx --reproducible
sha256sum output/MyApp-1.0.0.msi   # must match across both runs
```

To verify a *shipped* artifact against its source in one step — rebuild reproducibly and
byte-compare automatically — use [`forge verify --rebuild`](cli-json-schema.md#forge-verify---json):

```bash
forge verify MyApp-1.0.0.msi --rebuild installer.csproj   # -> VERIFIED / MISMATCH
```

---

## 2. SBOM Sidecars (CycloneDX 1.6)

### What it is

After every successful compile, FalkForge can write a CycloneDX 1.6 JSON SBOM alongside
the output artifact:

- `MyApp-1.0.0.msi.cdx.json` — MSI SBOM (payload files + hashes + product identity)
- `MyBundle.exe.cdx.json` — Bundle SBOM (embedded package hashes + product identity)

### Contents

Each SBOM includes:

| Field | Description |
|-------|-------------|
| `bomFormat` | `CycloneDX` |
| `specVersion` | `1.6` |
| `serialNumber` | Unique `urn:uuid:` per build (reproducible when `SOURCE_DATE_EPOCH` set) |
| `metadata.component` | Product name + version + manufacturer |
| `metadata.tools` | `FalkForge` (tool provenance) |
| `metadata.timestamp` | Build time (pinned via `SOURCE_DATE_EPOCH`) |
| `components[]` | One entry per payload file: `name`, `version`, `type`, SHA-256 `hashes` |
| User-supplied | Any `AddComponent()` entries from `SbomOptions` |

The SBOM uses no external NuGet package — it is written by `CycloneDxSbomGenerator` via
`Utf8JsonWriter` (AOT-safe, no reflection).

### Enabling SBOM

**Fluent API (recommended):**

```csharp
// MSI
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .Sbom(s => s
        .AddComponent("OpenSSL", "3.2.1", SbomComponentType.Library, sha256: "AABB..."))
    .Feature("Main", f => f.File("MyApp.exe")));

// Bundle
Installer.BuildBundle(b => b
    .Name("MyBundle").Version("1.0.0")
    .Sbom()   // payload hashes auto-populated from embedded packages
    .Chain(c => c.MsiPackage("output/MyApp-1.0.0.msi", p => p.Id("MyApp"))));
```

**CLI flag:**

```bash
forge build installer.csx --sbom   # generates SBOM from resolved files; no fluent config needed
```

**Environment variable:**

```bash
FALKFORGE_GENERATE_SBOM=1 forge build installer.csx
```

### Sidecar path

The sidecar is written to `<outputPath>.cdx.json` alongside the compiled artifact.

### Integrity-linked SBOM (Sigil)

When `BundleBuilder.Integrity()` is configured and the `sigil` CLI is on PATH, the bundle
SBOM is also wrapped in a Sigil DSSE attestation envelope and embedded inside the bundle
manifest as `SbomAttestation`. This is a separate (additive) path — the `.cdx.json` sidecar
is always written first and does not depend on Sigil.

Sigil is a separate, optional, build-time-only tool (a `Sigil.Sign` .NET global tool, not part
of FalkForge) — see [Sigil: Optional SBOM Attestation & MSI Integrity Signing](../documentation.html#sigil)
for what it is, install instructions, and the (more load-bearing) MSI-side behavior.

### Verification

```bash
# Validate CycloneDX JSON structure
cat MyApp-1.0.0.msi.cdx.json | python -m json.tool > /dev/null   # syntax check

# Verify a file hash matches a component entry
sha256sum MyApp.exe   # compare against components[].hashes[].content
```

---

## 3. ECDSA Payload Integrity

### What it is

`BundleBuilder.Integrity()` enables pure-.NET ECDSA-P256 signing of the bundle's payload
hash list. One or more signatures are embedded in the bundle manifest. The engine verifies
them at two points: before the Apply phase (`PayloadIntegrityGate`) and before any payload is
extracted (`SignedPayloadTocVerifier`) — both self-extract and the update path.

### What is signed

The signed envelope covers the ordered list of `(packageId, sha256Hash)` pairs for every
embedded payload, plus an optional `epoch` counter used for anti-downgrade (see below). The
envelope carries a **list** of signatures — `AddSigningKey`/`SigningKeys` on `IntegrityBuilder`
produce one signature entry per key, all over the identical file list, so a bundle can be
"dual-signed" during a key rotation window. Each signature entry self-describes its own public
key so the verifier needs nothing external to check the signature *math*; whether that key is
**trusted** is a separate question, answered by the trust anchor below.

### Threat model — read this before relying on it

The security property you get depends entirely on **which keys the engine trusts**, not on
whether the signature verifies. A signature always self-describes its key; anyone can produce
a *valid* signature with a key they just generated. Authorship requires the verifying engine to
compare that key's fingerprint against a set it did not get from the bundle.

#### Unpinned engines (empty trusted set) — *integrity, not authorship*

An engine built with no `-p:FalkForgeTrustedKey` and no code-registered key (see below) has an
empty trusted set. `PayloadIntegrityGate`/`SignedPayloadTocVerifier` then fall back to
**consistency-only** verification: any signature that verifies is accepted, regardless of whose
key it is. This proves:

- the payload bytes match their manifest hashes (cache layer), and
- those hashes are the ones covered by *a* signature, and
- every package that will run is in the signed set (set-coverage, see below).

It therefore **detects casual tampering in transit** — an attacker who flips bytes in a
payload, swaps a hash, or appends an unsigned package without re-signing is caught, because
the attacker's re-signed copy still has to pass the hash-binding and coverage checks — but an
attacker who fully rewrites the bundle can recompute every hash and re-sign with a fresh key
of their own, and consistency-only mode accepts it. Authorship is only established when the
engine carries a trusted set to check the key against — the two modes below.

> **Do not claim "an attacker cannot forge the signature" for an unpinned engine.** They can —
> by signing with their own key. Consistency-only mode is tamper-*evidence*, equivalent to a
> checksum the builder vouches for, not a publisher identity proof.

#### Baked trusted-key mode — *authorship, build-time*

A publisher builds their own `FalkForge.Engine.exe` passing the fingerprint(s) of their signing
key(s) as an MSBuild property, repeatable for a set:

```bash
dotnet publish src/FalkForge.Engine -c Release -p:FalkForgeTrustedKey=A1B2C3...
```

`TrustedKeys.targets` generates `BakedTrustedKeys.Fingerprints` — a `FrozenSet<string>` of
uppercase-hex SHA-256 SubjectPublicKeyInfo fingerprints — compiled directly into the engine
binary. It is never read from the bundle, the manifest, or a config file next to the EXE, so an
attacker who fully rewrites the bundle cannot also rewrite the trust anchor: the engine that
runs *is* the one the publisher shipped. A signature is trusted only when its key's fingerprint
is in this set; a bundle re-signed by an untrusted key is rejected (`INT001`).

#### Code-registered trust — `EngineTrustAnchor`

Trusted keys can also be registered from the engine's own compiled bootstrap code, additive to
the baked MSBuild set — useful when a publisher wants trust configuration alongside other
bootstrap logic instead of (or in addition to) the build-time property. `EngineTrustAnchor`
exposes `TrustPublicKey(ReadOnlySpan<byte> spki)`, `TrustPublicKeyPem(string pem)`, and
`TrustFingerprint(string fingerprint)` (normalizes separators/case, validates a 64-hex SHA-256
fingerprint, fail-loud on anything else). Registrations are only reachable from compiled code —
never from a bundle, manifest, downloaded update, or any file/network input the installer
processes, which is the same trust boundary the baked set relies on. The **effective** trusted
set is the union of the baked set and every code-registered key, computed once on first read and
frozen thereafter; a registration attempt after that first read throws, so trust can never widen
once verification has begun. Register early, before any bundle is touched.

### Set coverage (signed manifests are exhaustive)

Once a manifest is signed, the gate enforces verification in **both** directions:

- **signed → manifest:** every signed entry binds to a manifest package with a matching hash
  (`INT002` on mismatch or on a signed entry with no matching package; `INT003` if the entry
  itself is malformed, e.g. an empty name), and
- **manifest → signed:** every package in the manifest is in the signed set (`INT004`
  otherwise).

The second direction closes the gap where an attacker appends an unsigned package to a validly
signed bundle and has it execute alongside the signed ones. At extraction time
`SignedPayloadTocVerifier` enforces the same coverage rule over **every** table-of-contents
entry — not just chained install packages, but the bundle's own UI and engine binaries too —
so an attacker cannot tamper with the executables the bootstrapper itself launches by leaving
them out of the signed set (`INT004`).

### Fresh install vs. update — the policy seam

Two policies apply the same trust anchor differently:

- **Fresh install:** an unsigned bundle still runs — bundles built without `.Integrity()`
  predate the feature and must keep working, so the gate is an *additive* defense here, not a
  mandatory-signing requirement. A signature that fails to verify against the effective trusted
  set is still rejected (a present-but-untrusted signature is an attack signal, not a legacy
  bundle).
- **Update path (require-signed):** before the already-installed engine relaunches a downloaded
  update, `StagedUpdateVerifier` verifies the staged bundle **in-process**, with `RequireSigned`
  set. A missing or stripped signature is rejected (`INT007`) rather than passed through, and —
  because a require-signed check against an empty trusted set would silently fall back to
  accept-any — an empty effective set on this path is *also* rejected (`INT009`, fail closed)
  rather than treated as consistency-only. This is deliberately asymmetric: a fresh install is a
  user's deliberate choice to run a specific artifact; an automatic, unattended update replacing
  already-trusted software gets the stricter rule.

### Fluent API

```csharp
Installer.BuildBundle(b => b
    .Product("MyBundle", "1.0.0")
    // No SigningKey configured: a throwaway key is generated per build.
    // Proves tamper-evidence only — see "Unpinned engines" above.
    .Integrity(i => i)
    .Chain(c => c.MsiPackage("output/MyApp-1.0.0.msi", p => p.Id("MyApp"))));

Installer.BuildBundle(b => b
    .Product("MyBundle", "1.0.0")
    // A stable PEM key from the secret store: this key's fingerprint is what
    // publishers bake into the engine (-p:FalkForgeTrustedKey) for authorship.
    .Integrity(i => i.SigningKey("falkforge-signing.pem"))
    .Chain(c => c.MsiPackage("output/MyApp-1.0.0.msi", p => p.Id("MyApp"))));
```

`AddSigningKey`/`SigningKeys` add further keys for a dual-signed rotation window; `Epoch(int)`
and `Revoke(params string[])` feed the anti-downgrade/revocation data model described below.
See §22 for the full key-generation, baking, and rotation runbook.

### Engine gate

Before the Apply phase, `ApplyStep` reads `ctx.Manifest.ManifestSignature` and the pipeline's
`TrustPolicy` (defaulted to a fresh-install policy pinned to
`EngineTrustAnchor.EffectiveFingerprints`, overridable for tests). When a signature is present,
`PayloadIntegrityGate.Verify` checks it against the trust policy, the hash bindings, and set
coverage. Any failure produces `ErrorKind.IntegrityError` and aborts the installation before a
single package runs.

### Payload byte binding (extraction-time)

The gate above proves the manifest's package hashes are the ones the signature covers, but it
never touches a payload byte. The bytes live in the bundle's **appended overlay**: the manifest
JSON, the compressed payloads, and the **table of contents (TOC)** are all appended after the PE
stub. Authenticode (when used) is applied to the bare stub before the overlay is attached, and it
does not cover bytes appended after the signature, so the overlay — including the TOC — is
outside its coverage by construction. The ECDSA manifest signature covers the manifest's payload
hashes, **not** the TOC.

Runtime extraction (`BundleReader` for full payloads, `DeltaApplicator` for delta payloads)
verifies each payload's decompressed bytes against the **TOC** hash (`TocEntry.Sha256Hash`, or
`TocEntry.ReconstructedSha256Hash` for a reconstructed delta). That hash is in the unsigned
overlay. Left unbound, this is a hole: an attacker can take a validly-signed bundle, flip payload
bytes, rewrite the matching TOC hash, and leave the signed manifest and its signature untouched —
extraction verifies the tampered bytes against the tampered TOC hash, accepts them, and the
payload runs while the signature still verifies.

`SignedPayloadTocVerifier.Verify(manifest, tocEntries, trustedFingerprints, requireSigned, ...)`
closes this. Before any payload is extracted — the self-extract `--extract` path, the
bootstrapper's UI-launch path, and (with `requireSigned: true`) the staged-update verification
path in `Program.cs` — it re-verifies the envelope against the trust policy and then requires,
for **every** TOC payload (including the bundle's own UI/engine binaries — see "Set coverage"
above), that the value the extractor will trust equals the signed hash:

- **full payload** — `TocEntry.Sha256Hash` must equal the signed hash (bytes == TOC == signed), and
- **delta payload** — `TocEntry.ReconstructedSha256Hash` must equal the signed hash
  (reconstructed == ReconstructedSha256Hash == signed); the delta-blob hash is unsigned and
  irrelevant to trust.

A TOC hash that disagrees with the signed hash is a post-signing overlay tamper and is rejected
with `INT006` before a byte is extracted. So payload integrity comes from **the ECDSA manifest
signature plus this byte binding** — Authenticode alone proves nothing about the payloads.
Unsigned bundles have no signed hash to bind to and keep only TOC-level tamper detection.

### Anti-downgrade / revocation store — dormant this release

The trust anchor says *which keys* are trusted; it cannot by itself stop a **downgrade or
replay** of an older bundle signed by a key that was later revoked from newer engines but is
still in an already-installed engine's baked set. The data model for this exists today: a
signed envelope may declare an `epoch` and a `revoked[]` list, and a per-machine store
(`TrustState` / `TrustStateStore`, `%ProgramData%\FalkForge\Trust\trust-state.json`, ACL-hardened
to SYSTEM/Administrators-write) persists the highest epoch accepted and any revoked fingerprints,
enforced by both gates as `INT008` (epoch below stored epoch) and `INT001` (accepted key locally
revoked).

**This protection is not active in this release.** The store only advances after a verified
update apply, and that advance is issued by the engine bootstrapper, which runs `asInvoker`
(non-elevated). The restrictive store ACL denies that non-elevated write, so in a normal
standard-user run the epoch never advances past 0 and no revocation is ever recorded — the
`INT008`/revocation checks have nothing to enforce against yet. Do not rely on the engine
blocking a version downgrade or replay today. Activation is tracked as follow-up **C16**: move
the store write into the elevated companion (`FalkForge.Engine.Elevation`) so every advance is
written elevated, with ACL validation on load.

### Artifact location

The signature(s) are embedded in the bundle manifest (inside the EXE) — no external file. The
trust anchor that decides whether those signatures are *authoritative* lives in the engine
binary itself: the MSBuild-baked `BakedTrustedKeys` set, unioned with anything registered via
`EngineTrustAnchor` at bootstrap. Neither is read from the bundle, the artifact being verified,
or any file the installer processes at runtime.

### MSI signing and verification — `forge verify`

Everything above describes bundle *runtime* verification: an installed EXE bundle has an engine
in the loop that checks the signature before executing a single payload. An MSI has no such
engine — Windows Installer itself has no concept of FalkForge's ECDSA envelope — so
`PackageBuilder.Integrity()` (the identical fluent API and identical `EcdsaManifestSigner`/
`IntegrityEnvelopeCodec` signing code as bundles) instead produces an envelope that is verified
**out-of-band, after the fact**, via the CLI:

```bash
forge verify MyApp-1.0.0.msi                          # consistency-only (tamper-evidence)
forge verify MyApp-1.0.0.msi --trusted-key A1B2C3...   # authorship (repeatable flag)
```

**What is signed.** Identical shape to bundles: the ordered `(name, sha256)` list of every payload
file FalkForge embeds in the MSI, keyed by the file's target name (the `File` table's `FileName`
column) and hashed from the *original source file on disk* at build time — not from any MSI
container byte. `forge verify` recomputes this independently at verification time by re-extracting
every embedded cabinet and re-hashing each file, then binds that recomputed hash back to the
signed declaration **bidirectionally**:

- **signed → actual** (every declared file is present with a matching hash) — the same direction
  `FalkForge.Engine.Integrity.PayloadIntegrityGate` enforces for bundles at install time; and
- **actual → signed** (every embedded payload file is in the declared set) — this direction is
  what stops an attacker from taking a genuinely signed, trusted MSI and *adding* an extra,
  undeclared payload file without touching anything the signature covers. Checking only the first
  direction would let that addition through as `VERIFIED`, carrying the real publisher's
  fingerprint, because every declared file still matches — MSI has no separate runtime gate the
  way a bundle's engine does, so `forge verify` is the only place this is ever caught.

A payload swapped into, or added to, the MSI after signing — leaving the signature table/sidecar
itself untouched — is caught by this binding even though the signature cryptographically still
verifies against its own (unchanged) declaration.

**Duplicate-name collisions are always tamper.** The envelope's `(name, sha256)` pairs are
NAME-ONLY granularity — it has no way to express "there must be exactly one embedded payload file
named X." If two or more actual embedded payload files resolve to the same name (a distinct `File`
table row and cabinet entry per copy, but an aliased `FileName`), `forge verify` refuses the MSI as
tamper unconditionally — it never picks either copy's hash to reconcile with the declaration, even
if one of them happens to match. This closes a variant of the addition attack above: without it, an
attacker could splice in a *second* copy of an already-declared file name with malicious bytes, and
if that copy is processed before the legitimate one during cabinet re-extraction, a naive
"last-write-wins" accumulation would silently retain only the legitimate hash — passing both
direction checks above as `VERIFIED` while the genuinely separate, malicious `File` row still
installs via `msiexec`. Legitimate MSIs cannot trigger this: the compiler enforces case-insensitive
`FileName` uniqueness at build time, so a name collision is reachable only via direct post-build
database tampering.

> **Known limitations.** Only *embedded* cabinets (`Media.Cabinet` prefixed `#`) are re-extracted
> for the content-binding check — the same limitation `forge extract` already has. A payload
> shipped via an external, disk-resident cabinet is neither confirmed nor contradicted by it.
> The envelope covers embedded payload FILES only — it says nothing about the content of other
> MSI database tables (e.g. `Registry`, `CustomAction`, `Property` rows), so an attacker who edits
> those directly, without adding or altering a payload file, is not detected. The signature check
> also covers the CLASSICAL (ECDSA-P256) entry only: a hybrid post-quantum (ML-DSA) companion
> signature, if present, is neither verified nor required for MSI today — there is no
> `--pq-key`/`pqPolicy` equivalent of the bundle engine's `INT011` enforcement here yet.

**Where the signature lives — table vs. sidecar.** `IntegritySigner` always writes the envelope to
a detached `<msi>.sig.json` sidecar next to the compiled MSI. Whether it *also* embeds the
identical envelope in-band, in the MSI's own `_FalkForgeIntegrity`/`ManifestSignature` custom
table, depends on `Reproducible()`:

| Build configuration | In-band table | Sidecar | MSI bytes |
|---|---|---|---|
| `Integrity()` only | Yes | Yes | Non-deterministic (ECDSA signature is part of the MSI) |
| `Reproducible()` + `Integrity()` | **No** | Yes | Byte-identical across builds |

A `Reproducible()`+`Integrity()` MSI never carries the signature in-band — embedding a
non-deterministic ECDSA signature in the MSI's own database would silently break
`forge verify --rebuild` for that exact combination (the compiled bytes could never byte-match a
prior build even from identical source). `forge verify` handles both shapes transparently: it
prefers the in-band table when present, and falls back to the sidecar when it is not — the
sidecar path is a normal, fully-supported verification, not a degraded one.

**Verdicts:** `VERIFIED` (exit 0) — but rendered under two distinct labels: **authorship
verified** (green — a `--trusted-key` matched) versus **tamper-evidence only** (yellow — no
`--trusted-key` was supplied, so publisher identity was never checked). The two must never render
identically; a consistency-only pass is a strictly weaker claim than an authorship-verified one,
and printing them the same way would be a downgrade-attack UX. `NOT-SIGNED` (exit 1 — no table and
no sidecar; fail-loud, an unsigned MSI is never reported as passing), `FAILED` (exit 1 — signature
invalid, matched no trusted key, the recomputed content no longer matches what was signed in
either direction — missing, added, or altered files — or a located sidecar exceeded the 4 MiB read
cap FalkForge enforces against DoS/corruption). See
[`cli-json-schema.md`](cli-json-schema.md#forge-verify---json) for the full envelope shape
(including the `authorshipEstablished` field JSON consumers should key off of) and exit-code table.

**`forge inspect`** additionally surfaces signature *presence*, the format tag (e.g.
`falkforge-ecdsa-envelope-v2`), and the declared signing-key fingerprint(s) for quick,
non-cryptographic display — actual verification is `forge verify`'s job, not `forge inspect`'s.
Classical (ECDSA-P256) fingerprints — the ones `--trusted-key` matches — are shown separately from
any hybrid post-quantum companion fingerprint, under a distinct label, so the two are never
confused: pasting a PQ companion fingerprint into `--trusted-key` would otherwise produce a
baffling `INT001`.

---

## 4. WinGet Manifest Generation

### What it is

FalkForge auto-generates a WinGet installer manifest (3-file YAML) alongside MSI output so
packages can be published to the Windows Package Manager without manual YAML authoring.

### Fluent API

```csharp
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .WinGet(w => w
        .PackageIdentifier("Contoso.MyApp")
        .InstallerUrl("https://releases.contoso.com/v1.0.0/setup.msi")
        .License("MIT")
        .ShortDescription("A productivity tool for developers"))
    .Feature("Main", f => f.File("MyApp.exe")));
```

### CLI

```bash
forge build installer.csx          # generates .winget.yaml automatically when WinGet() is configured
forge winget existing.msi          # generate manifest from an existing compiled MSI
```

### Output files

| File | Description |
|------|-------------|
| `{name}.winget.yaml` | Installer manifest (WinGet manifest type: installer) |
| `{name}.winget-version.yaml` | Version manifest |
| `{name}.winget-locale.yaml` | Default locale manifest |

Fields auto-populated: `InstallerSha256` (computed at compile time), `PackageVersion`,
`InstallerType`, `Architecture`, `ProductCode`.

---

## 5. Plan Export (Headless Dry-Run)

### What it is

`forge plan` compiles the installer, runs the engine detection + planning phases only
(no elevation, no installation), and emits a machine-readable JSON summary of what the
installer would do.

### Use cases

- Change management approval workflows
- CI/CD diff-based auditing
- Pre-flight checks before deploying to a managed fleet

### CLI

```bash
forge plan installer.csx              # JSON to stdout
forge plan installer.csx -o plan.json # Write to file
forge plan installer.csx | jq '.packages[].action'
```

### Output format

```json
{
  "action": "install",
  "packages": [
    {
      "id": "MyApp.msi",
      "type": "MsiPackage",
      "action": "install",
      "version": "1.0.0"
    }
  ],
  "extensionActions": [
    { "description": "Add URL reservation http://+:8080/ for Network Service", "kind": "Network" }
  ],
  "requiresElevation": true,
  "requiresReboot": false
}
```

### Engine behaviour

The plan-only run executes `Initializing → Detecting → Planning → Shutdown`. It never
enters the `Elevating` or `Applying` phases and makes no system changes.

---

## 6. Bundle Dry-Run Mode

### What it is

Any FalkForge bundle EXE supports `--dry-run` at runtime. The full installer UI launches.
The user clicks through normally and hits Install — but the engine simulates the Apply phase
instead of running any package installer.

### Runtime use (no special build required)

```bash
MyInstaller.exe --dry-run
```

### Baked-in dry-run (always-simulate test builds)

```csharp
Installer.BuildBundle(b => b
    .Name("TestBundle").Version("1.0.0")
    .DryRun()   // bakes --dry-run into the manifest; runtime flag takes precedence
    .Chain(c => c.MsiPackage("output/MyApp-1.0.0.msi", p => p.Id("MyApp"))));
```

When `IsDryRun` is set in the manifest, `InstallerPipeline` seeds `PipelineContext.IsDryRun`
at startup so `ApplyStep` simulates from the first Apply request.

### Extension dry-run support

All built-in extensions implement `IDryRunContributor` and return human-readable action
descriptions. If any registered extension does NOT implement `IDryRunContributor`, dry-run
is blocked with `PLN004` and a clear list of unsupported extension names.

| Extension | Install dry-run action |
|-----------|----------------------|
| Http | "Add URL reservation … / Add SNI SSL binding …" |
| Firewall | "Add firewall rule: {name} ({protocol} {port})" |
| IIS | "Create app pool / web site / binding" |
| SQL | "Create database / Execute script" |
| Util | "Configure XML / Create user / Create file share" |
| Dependency | "Register dependency provider key(s) in registry" |
| DotNet | "Detect .NET runtime via registry and filesystem" |
| Driver | "Install device driver(s) via pnputil" |

---

## Artifact Summary

| Artifact | Trigger | Path | Verifiable by |
|----------|---------|------|---------------|
| MSI SBOM | `.Sbom()` fluent / `--sbom` / env var | `{msi}.cdx.json` | CycloneDX tooling / `jq` |
| Bundle SBOM | `.Sbom()` fluent / `--sbom` / env var | `{exe}.cdx.json` | CycloneDX tooling / `jq` |
| ECDSA signature (bundle) | `.Integrity()` fluent | Embedded in EXE manifest | Engine gate at Apply |
| ECDSA signature (MSI) | `.Integrity()` fluent | `_FalkForgeIntegrity` table and/or `{msi}.sig.json` sidecar (sidecar-only under `Reproducible()`) | `forge verify {msi}` |
| WinGet manifest | `.WinGet()` fluent / `forge winget` | `{name}.winget*.yaml` | WinGet CLI validation |
| Plan JSON | `forge plan` | stdout / `-o <file>` | `jq` / change management tools |
| Reproducible build | `.Reproducible()` fluent | N/A (determinism property) | `sha256sum` across two builds |
| GitHub build provenance | Release workflow (automatic on `v*` tag) | GitHub Attestations API | `gh attestation verify <file> --repo Falkesand/FalkForge` |

---

## 7. GitHub Release Attestations

### What it is

Every release build produced by the [release workflow](../.github/workflows/release.yml)
generates a SLSA-style build provenance attestation via
[`actions/attest-build-provenance`](https://github.com/actions/attest-build-provenance).
The attestation records the exact workflow run, commit SHA, repository, and artifact digests
in a signed statement stored in the GitHub Attestations API.

This is complementary to the compile-time provenance features (SBOM, ECDSA payload
integrity, reproducible builds) — it attests to the *build environment* rather than the
*installer content*.

### What is attested

- `FalkForge.Engine.exe` — NativeAOT bundle engine
- `FalkForge.Engine.Elevation.exe` — NativeAOT elevated companion process
- `forge.exe` — CLI tool
- `SHA256SUMS.txt` — checksum manifest for all release files

### Verification

```bash
# Verify a downloaded artifact before running it
gh attestation verify forge.exe --repo Falkesand/FalkForge
gh attestation verify FalkForge.Engine.exe --repo Falkesand/FalkForge
gh attestation verify FalkForge.Engine.Elevation.exe --repo Falkesand/FalkForge
```

A successful verify confirms:
- The file was produced by a GitHub Actions workflow in `Falkesand/FalkForge`.
- The workflow ran against a specific commit SHA (visible in the attestation output).
- The file has not been tampered with since it was uploaded.

### Private repo note

GitHub build provenance attestations require a paid plan for private repositories
using GitHub-hosted runners. While this repository is private, the attestation step
runs with `continue-on-error: true` — artifacts are still released and the workflow
writes a warning to the summary if attestation fails. Enforcement will be unconditional
once plan support is confirmed or the repository is made public.

---

## Error Codes

| Code | Description |
|------|-------------|
| INT001 | No trusted signature validates the manifest (tampering, untrusted publisher key, or locally-revoked key) |
| INT002 | Signed hash does not match the manifest package hash, or a signed entry has no matching package |
| INT003 | Malformed integrity envelope (parse failure, no signatures, or embedded manifest deserialization failure) |
| INT004 | A manifest package, or a bundle TOC payload, is not covered by the integrity signature (set-coverage violation) |
| INT006 | Bundle TOC payload hash does not match the ECDSA-signed manifest hash (post-signing overlay tamper) |
| INT007 | A signature is required on this path (e.g. update) but the manifest carries none |
| INT008 | Bundle key-epoch is below the highest epoch this machine has accepted (downgrade/replay) — dormant, see §3 |
| INT009 | A signature is required but the engine's effective trusted set is empty (fail closed) |
| SBM001 | Failed to compute SHA-256 hash for SBOM component |
| SBM002 | Failed to write SBOM output file |
| PLN001 | Detection phase failed during plan-only mode |
| PLN002 | Planning phase failed during plan-only mode |
| PLN003 | Failed to serialize plan to JSON |
| PLN004 | Dry-run mode blocked: one or more extensions do not support it |
