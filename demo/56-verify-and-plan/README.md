# Demo 56: Verify and Plan — Provability Commands

Shows the three FalkForge "provability" CLI commands: `forge verify`, `forge plan-diff`, and `forge plan`.
These commands let you inspect, compare, and independently prove the integrity of installer artifacts
**before** you run them.

## What These Commands Are For

| Command | What it does |
|---------|-------------|
| `forge verify --rebuild` | Proves a shipped artifact is what it claims to be: rebuilds the project from source and compares the output byte-for-byte. If the bytes match, the installer was not tampered with after build. |
| `forge plan-diff` | Shows exactly what changed between two versions of an installer. Add a file, bump a version, change a service — the diff shows every difference before you ship the update. |
| `forge plan` | Shows what the installer will do before you run it: which packages will be installed, in what order, and with what actions. Works on bundle EXE files. |

## Why Reproducible Builds Matter

`forge verify` only works if the installer was built with `.Reproducible()`. Without it, each build
embeds a fresh random PackageCode GUID, and two builds from identical source will not be byte-identical.

This demo enables `Reproducible()` so the same source always produces the same MSI output.

## How to Build

```bash
dotnet build demo/56-verify-and-plan/56-verify-and-plan.csproj
```

## Running the Commands

> **Note:** `Reproducible()` requires the `SOURCE_DATE_EPOCH` environment variable to be set.
> Set it once before running any commands in this demo:
>
> ```powershell
> $env:SOURCE_DATE_EPOCH = "1700000000"
> ```
>
> `forge verify --rebuild` sets this automatically when it rebuilds. You only need to set it
> manually when building the artifact yourself (the steps below).

### forge verify — prove the artifact matches its source

Run these commands from the `demo/56-verify-and-plan/` directory (payload paths resolve relative to CWD):

```bash
# Build the artifact
dotnet run --project 56-verify-and-plan.csproj -- -o out/v1
```

Then run `forge verify` from the repo root, pointing at the built artifact and the project:

```bash
forge verify demo/56-verify-and-plan/out/v1/Provability_Demo-1.0.0.msi \
    --rebuild demo/56-verify-and-plan/56-verify-and-plan.csproj
```

**Actual output:**
```
Rebuilding 56-verify-and-plan.csproj (SOURCE_DATE_EPOCH=1700000000)...
VERIFIED: rebuilt artifact is byte-identical (459 220 bytes). The artifact
provably came from this source.
```

If you see `MISMATCH`, either the build is not reproducible (missing `.Reproducible()`) or the artifact
was modified after build (e.g. code-signed). Signing deliberately changes bytes; that is expected and
the command reports the signed status alongside the verdict.

### forge plan-diff — see what changed between two versions

```bash
# From demo/56-verify-and-plan/:
dotnet run --project 56-verify-and-plan.csproj -- -o out/v1
# Edit Program.cs (e.g. bump Version to 2.0.0), then:
dotnet run --project 56-verify-and-plan.csproj -- -o out/v2
```

```bash
# From repo root:
forge plan-diff demo/56-verify-and-plan/out/v1/Provability_Demo-1.0.0.msi \
                demo/56-verify-and-plan/out/v2/Provability_Demo-1.0.0.msi
```

**Actual output (identical source, no changes):**
```
MSI plan diff:
old/Provability_Demo-1.0.0.msi → new/Provability_Demo-1.0.0.msi
No differences found.
```

**Expected output when version is bumped to 2.0.0:**
```
Package changes
  ~ Provability Demo   1.0.0 → 2.0.0
```

Use `--markdown` to get output suitable for pasting into a GitHub PR comment:

```bash
forge plan-diff old.msi new.msi --markdown
```

### forge plan — inspect the install plan (bundle EXE only)

```bash
forge plan path/to/MyBundle.exe
```

`forge plan` reads the embedded manifest from a **bundle EXE** (not an MSI) and computes what the
installer would do without actually running it. It requires the FalkForge Engine binary to be present.

This demo produces an MSI. See [demo/35-bundle-simple](../35-bundle-simple) for the bundle project
shape. Once you have a bundle EXE:

```bash
forge plan MyBundle.exe
```

**Expected output:**
```
Plan — 2 package action(s)
  1. Install   Runtime.msi       (vital)
  2. Install   MyApp.msi         (vital)
```

## Running the Full Script

The included `provability.ps1` script runs all steps in sequence:

```powershell
# From the repo root:
pwsh demo/56-verify-and-plan/provability.ps1
```

The script builds the MSI twice, runs `forge verify --rebuild` to confirm reproducibility, then
runs `forge plan-diff` between the two builds (identical source → "No changes detected").

## Key API Calls

| Method | Purpose |
|--------|---------|
| `package.Reproducible()` | Pin PackageCode to a content-derived UUID so the same source always produces identical bytes. Required for `forge verify`. |
| `package.UseDialogSet(MsiDialogSet.Minimal)` | Minimal install UI — no feature selection or directory prompt. |
| `package.MediaTemplate(mt => mt.EmbedCabinet(true))` | Embed the cabinet file inside the MSI (single-file distribution). |

## Exit Codes

### forge verify
| Code | Verdict | Meaning |
|------|---------|---------|
| 0 | VERIFIED | Byte-identical — artifact matches source. |
| 1 | MISMATCH | Bytes differ — artifact was modified after build (or build is not reproducible). |
| 2 | REBUILD-FAILED | The project did not build successfully. |
| 3 | SETUP-ERROR | Build succeeded but produced no artifact of the expected type. |

### forge plan-diff
| Code | Meaning |
|------|---------|
| 0 | Diff completed (differences may or may not exist — check output). |
| 3 | Runtime error: missing file, mismatched artifact types, or platform error. |

### forge plan
| Code | Meaning |
|------|---------|
| 0 | Plan produced successfully. |
| 3 | Runtime error: bundle not found, manifest missing, Engine binary not found. |

## Notes

- `forge verify --rebuild` runs `dotnet run --project <csproj> -- -o <tempdir>` with `SOURCE_DATE_EPOCH`
  pinned. The rebuild takes the same time as a normal build (up to 5 minutes timeout).
- If the artifact is code-signed, `forge verify` reports `signed: true` and a `MISMATCH` verdict.
  That is correct — signing intentionally modifies bytes. Verify before signing, not after.
- `forge plan-diff` works on both MSI and bundle EXE files, but both arguments must be the same type.
  Cross-format diffing (one MSI, one EXE) returns exit code 3.
- `forge plan` is bundle-only and requires the FalkForge Engine binary. It does not work on MSI files.
