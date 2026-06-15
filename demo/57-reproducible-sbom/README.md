# Demo 57: Reproducible Builds + SBOM

A minimal installer that demonstrates three interrelated supply-chain features: reproducible
builds, SBOM generation, and (for bundles) ECDSA payload integrity. Together they let anyone
independently verify that a shipped installer came from the stated source and contains exactly
what it claims to contain.

## What This Demonstrates

- Pinning all MSI timestamps to `SOURCE_DATE_EPOCH` with `Reproducible()` so two builds of the
  same source produce byte-identical output
- Emitting a CycloneDX SBOM sidecar (`.cdx.json`) with `Sbom()`, listing every installed file
  with its SHA-256 hash
- Making the SBOM itself reproducible — the serial number and timestamp are derived from build
  content when `SOURCE_DATE_EPOCH` is active, not from the wall clock
- Declaring additional components (e.g. bundled runtimes) in the SBOM via `AddComponent()`
- Using `forge verify --rebuild` to independently prove that a shipped artifact matches its source

## Key API Calls

```csharp
// Pin all MSI timestamps to SOURCE_DATE_EPOCH — same source = same bytes, every time.
package.Reproducible();

// Emit a CycloneDX SBOM sidecar (.cdx.json) alongside the MSI.
// Serial number + timestamp are content-derived when SOURCE_DATE_EPOCH is active.
package.Sbom(sbom => sbom
    .AddComponent(
        name: "Microsoft .NET Runtime",
        version: "10.0.0",
        type: SbomComponentType.Library,
        sha256: "<hash>",
        publisher: "Microsoft"));
```

## The Provability Workflow

### Step 1 — Build with a fixed epoch

```bash
set SOURCE_DATE_EPOCH=1700000000
dotnet run --project demo/57-reproducible-sbom -- -o ./out-a
```

This produces `out-a/Reproducible_SBOM_Demo-1.0.0.msi` and `out-a/Reproducible_SBOM_Demo-1.0.0.cdx.json`.

### Step 2 — Build again (must be byte-identical)

```bash
dotnet run --project demo/57-reproducible-sbom -- -o ./out-b
```

Compare:

```bash
# Windows PowerShell
(Get-FileHash out-a\Reproducible_SBOM_Demo-1.0.0.msi).Hash
(Get-FileHash out-b\Reproducible_SBOM_Demo-1.0.0.msi).Hash
# Both hashes must match.
```

### Step 3 — Independent verification with the CLI

```bash
forge verify out-a\Reproducible_SBOM_Demo-1.0.0.msi --rebuild demo\57-reproducible-sbom\Program.cs
```

`forge verify` rebuilds from source in reproducible mode and byte-compares the result against the
supplied artifact. Exit code `0` means **VERIFIED** — the artifact is provably the output of that
source. Exit `1` means **MISMATCH** (bytes differ, something changed). Exit `2` means the rebuild
itself failed.

### Step 4 — Inspect the SBOM

```bash
forge inspect out-a\Reproducible_SBOM_Demo-1.0.0.msi --extract-sbom
```

Or open `out-a/Reproducible_SBOM_Demo-1.0.0.cdx.json` directly. Each installed file appears as a
CycloneDX component with its SHA-256 hash.

### Using the CLI flags instead of code

```bash
forge build demo\57-reproducible-sbom\Program.cs --reproducible --sbom -o ./out-a
```

`--reproducible` sets `SOURCE_DATE_EPOCH` from the env var or git HEAD commit time.
`--sbom` sets `FALKFORGE_GENERATE_SBOM=1`, triggering SBOM generation even without a `Sbom()` call
in the script.

## ECDSA Payload Integrity (bundles)

When building EXE bundles, FalkForge signs each embedded payload with an ECDSA P-256 key and stores
the signature in the bundle table of contents. The installer engine verifies every payload's
signature before executing it. This means that even if an attacker replaces bytes inside the bundle
after signing, the engine will detect the tampering and refuse to proceed. For MSI-only packages,
the equivalent protection is `forge verify --rebuild`.

## Notes

- `SOURCE_DATE_EPOCH` is a Unix timestamp (seconds since 1970-01-01 UTC). Set it to any fixed
  integer for reproducible builds.
- The SBOM serial number is a deterministic RFC 4122 v5 UUID derived from the build content when
  `SOURCE_DATE_EPOCH` is active, so it is stable across rebuilds of the same source.
- `AddComponent()` is for components you distribute alongside the installer but that FalkForge
  cannot discover automatically (e.g. a bundled runtime, a redistributable DLL). Installed files
  are discovered and hashed automatically.
- The SHA-256 in `AddComponent()` should be the hash of the actual binary. The placeholder `0000…`
  in this demo is intentional — replace it with a real hash in production.
