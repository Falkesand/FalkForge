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
hash list. The signature is embedded in the bundle manifest. The engine verifies the
signature before the Apply phase.

### What is signed

The signed envelope covers the ordered list of `(packageId, sha256Hash)` pairs for every
embedded payload. Any tampered payload (or added/removed package) fails verification.

### Fluent API

```csharp
Installer.BuildBundle(b => b
    .Name("MyBundle").Version("1.0.0")
    .Integrity(i => i
        .WithEphemeralKey()        // dev / test
        // or .WithPemKey(privateKeyPem)  // production: load from vault
    )
    .Chain(c => c.MsiPackage("output/MyApp-1.0.0.msi", p => p.Id("MyApp"))));
```

### Engine gate

Before the Apply phase, `ApplyStep` reads `ctx.Manifest.ManifestSignature`. When present,
it verifies the ECDSA signature against the embedded payload hashes. Any mismatch produces
`ErrorKind.SecurityError` and aborts the installation.

### Artifact location

The signature is embedded in the bundle manifest (inside the EXE). No external file.

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
| ECDSA signature | `.Integrity()` fluent | Embedded in EXE manifest | Engine gate at Apply |
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
| SBM001 | Failed to compute SHA-256 hash for SBOM component |
| SBM002 | Failed to write SBOM output file |
| PLN001 | Detection phase failed during plan-only mode |
| PLN002 | Planning phase failed during plan-only mode |
| PLN003 | Failed to serialize plan to JSON |
| PLN004 | Dry-run mode blocked: one or more extensions do not support it |
