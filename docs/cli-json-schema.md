# forge --json Output Schema

The `forge` CLI emits a single structured JSON document on stdout when invoked with `--json`. CI pipelines, IDE integrations, and other automation should parse this envelope rather than scraping the human-oriented Spectre.Console rendering.

The `--json` flag is supported on:

| Command | Status |
|---------|--------|
| `forge build` | implemented |
| `forge validate` | implemented |
| `forge inspect` | implemented (Windows-only) |
| `forge plan` | implemented — extracts bundle manifest, launches engine headless in plan-only mode, renders package action summary |
| `forge verify` | implemented — rebuilds the source project reproducibly and byte-compares against a shipped artifact; emits a VERIFIED/MISMATCH/REBUILD-FAILED/SETUP-ERROR verdict |
| `forge rules list` | implemented — raw JSON array (not the common envelope; see [forge rules list --json](#forge-rules-list---json)) |

Source of truth: [`src/FalkForge.Cli/JsonConsoleOutput.cs`](../src/FalkForge.Cli/JsonConsoleOutput.cs) and [`src/FalkForge.Cli/Commands/`](../src/FalkForge.Cli/Commands/).

## Common Envelope

Every `--json` invocation produces exactly one JSON object on stdout, terminated by a single newline. Field order is deterministic. The envelope is non-indented (one line) for stream-friendly consumption.

```json
{
  "version": 1,
  "command": "<command name>",
  "exitCode": 0,
  "messages": [
    { "level": "info", "text": "..." }
  ],
  "result": {
    "key": "value"
  }
}
```

### Envelope fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `version` | integer | yes | Wire-format version. Currently `1`. Incremented only on breaking schema changes. See `JsonConsoleEnvelope.CurrentVersion`. |
| `command` | string | yes | Name of the command that produced the envelope. One of: `build`, `validate`, `inspect`, `plan`. |
| `exitCode` | integer | yes | Process exit code. See [Exit Code Reference](#exit-code-reference). |
| `messages` | array&lt;Message&gt; | yes | Ordered list of structured messages emitted by the command. May be empty. |
| `result` | object&lt;string, string \| null&gt; | no | Per-command key/value map. Omitted when null or empty (serializer uses `WhenWritingNull`). Currently no command populates this map; reserved for future use (e.g. output paths). |

### Message object

Each entry in `messages` represents one line of console output captured during the run. Spectre.Console markup tags such as `[red]...[/]` are stripped, and the leading colour tag (if any) is mapped to a structured `level`.

```json
{ "level": "info", "text": "Validation passed." }
```

| Field | Type | Description |
|-------|------|-------------|
| `level` | string | One of `info`, `warning`, `error`, `debug`. |
| `text` | string | Human-readable text with all Spectre markup tags removed. Validation rule IDs (e.g. `Warning PKG003:`) and command-specific prefixes are preserved as part of the text. |

### Tag → level mapping

| Spectre tag | `level` |
|-------------|---------|
| `[red]` | `error` |
| `[yellow]` | `warning` |
| `[green]` | `info` |
| `[grey]` / `[gray]` | `debug` |
| any other / no tag | `info` |

`WriteError` always emits an `error`-level message regardless of markup. `WriteLine` always emits `info`.

## forge build --json

**Purpose:** compile a `.cs` / `.csx` / `.json` installer definition into an MSI (and optionally a WinGet manifest).

**Envelope:** standard. The `result` map is currently empty.

**Representative messages:**
- `info` — `Loaded JSON config: <name> v<version>` (JSON inputs only)
- `info` — `Build succeeded: <output path>`
- `info` — `Signed bundle created: <output path>` (JSON inputs with a `signing` section: the MSI is wrapped in an EXE bundle whose integrity manifest is signed via the configured provider)
- `info` — `WinGet manifest written alongside installer` (when `--winget`)
- `warning` — `WinGet manifest generation failed: <reason>`
- `warning` — signing security warnings (SignServer `http://` base URL, `authMode: none`)
- `error` — script load / compilation failure messages, surfaced from `Result<T>.Error.Message`
- `error` — signing config errors `JSN015`–`JSN018` (structural validation) and `JSN019` (unresolvable key/auth material at build time; the build fails closed)

**Exit codes:**
- `0` — build succeeded
- `1` — validation / configuration failure (`ErrorKind.Validation`, `InvalidConfiguration`, MSIX-from-JSON rejection)
- `2` — compilation error from `MsiCompiler`
- `3` — runtime errors (file not found, non-Windows host, IO error, reproducible-build prerequisites missing)

**Example:**

```bash
forge build installer.csx --json
```

```json
{"version":1,"command":"build","exitCode":0,"messages":[{"level":"info","text":"Build succeeded: D:\\out\\Demo.msi"}]}
```

## forge validate --json

**Purpose:** load a `.cs` / `.csx` / `.json` definition and run `ModelValidator.Inspect`, or run ICE validation against a `.msi` file when `--ice` is set.

**Envelope:** standard. The `result` map is empty.

**Representative messages:**

Model validation:
- `warning` — `Warning <RuleId>: <message>` for every entry in `validation.Warnings`
- `error` — `Error <RuleId>: <message>` for every entry in `validation.Errors`
- `error` — `Validation failed with <N> error(s).` (terminal summary on failure)
- `info` — `Validation passed.` (terminal summary on success)

ICE validation (only when input is `.msi` and `--ice` is set):
- `warning` — `[yellow]Use --ice flag...[/]` if `--ice` was omitted on a `.msi` input (Spectre tag → `warning`)
- `error` / `warning` / `debug` — `<Severity> <IceName>: <description> (<table>)` per ICE message
- `info` / `error` — terminal summary `<N> issue(s) (<errors>, <warnings>). Validation PASSED|FAILED.`

**Exit codes:**
- `0` — validation passed
- `1` — validation failures present (model errors, or ICE failures with `IsValid == false`)
- `3` — runtime error (file not found, non-Windows host for ICE, ICE engine failure)

**Example:**

```bash
forge validate installer.csx --json
```

```json
{"version":1,"command":"validate","exitCode":1,"messages":[{"level":"warning","text":"Warning PKG003: Manufacturer should be set."},{"level":"error","text":"Error FEA001: Package must contain at least one feature."},{"level":"error","text":"Validation failed with 1 error(s)."}]}
```

## forge inspect --json

**Purpose:** open an `.msi` and report summary information (product name, manufacturer, version, product code, table count). Windows-only; uses `msi.dll`. With `--extract-sbom`, emits a previously embedded CycloneDX SBOM as a single `info` message.

**Envelope:** standard. The `result` map is empty.

**Representative messages (default mode):**
- `info` — `MSI: <path>` (rendered from `[bold]` markup)
- `info` — `Product: <ProductName | "(unknown)">`
- `info` — `Manufacturer: <Manufacturer | "(unknown)">`
- `info` — `Version: <Version | "(unknown)">`
- `info` — `Product Code: <ProductCode | "(unknown)">`
- `info` — `Tables: <TableCount>`
- `info` — `Integrity Signature: present (<SignatureFormatTag>)` or `Integrity Signature: not present`
- when present: one `info` — `Signing Key Fingerprint: <fingerprint>` per entry in `SignatureFingerprints`
- when present and the envelope carries a hybrid post-quantum companion: one `info` —
  `PQ Companion Fingerprint (not usable with --trusted-key): <fingerprint>` per entry in
  `PqCompanionFingerprints` — see [`forge verify --json`](#forge-verify---json) for why the two are
  never interchangeable
- with `--verbose`: `info` — `Table list:` followed by one `info` per name in `TableNames`

The underlying inspection is captured by `MsiInspectionResult` (see `src/FalkForge.Cli/MsiInspectionResult.cs`):

| Field | Type | Description |
|-------|------|-------------|
| `ProductName` | string? | Summary info `Subject` / `ProductName` property |
| `Manufacturer` | string? | `Manufacturer` property |
| `Version` | string? | `ProductVersion` property |
| `ProductCode` | string? | `ProductCode` property GUID |
| `TableNames` | array&lt;string&gt; | All table names in the MSI database |
| `TableCount` | integer | `TableNames.Count` |
| `SignaturePresent` | boolean | True when the embedded `_FalkForgeIntegrity` table or a detached `<msi>.sig.json` sidecar carries a signature. Non-cryptographic — presence only, not verification (`forge verify` is the verification path). |
| `SignatureFormatTag` | string? | The signature row's `Format` column (e.g. `falkforge-ecdsa-envelope-v2`), or null when `SignaturePresent` is false or the signature came from a sidecar (no format column), or when a located sidecar was refused for exceeding the 4 MiB read cap. |
| `SignatureFingerprints` | array&lt;string&gt; | Declared fingerprint(s) of the envelope's CLASSICAL (ECDSA-P256) signature entries — the ones a `forge verify --trusted-key` value must match. Displayed as written by the signer, not re-derived or checked against a trust anchor. |
| `PqCompanionFingerprints` | array&lt;string&gt; | Declared fingerprint(s) of any hybrid post-quantum (ML-DSA) companion signature entries. Deliberately a separate field from `SignatureFingerprints`: a zero-config `Integrity()` build on a PQ-capable machine signs with both a classical and an ML-DSA key, and `--trusted-key` only ever matches the classical one — mixing the two under one field/label is a copy-paste footgun that produces a baffling `INT001`. |

These values currently surface as text in `messages` rather than as a structured `result` object. Consumers that need the raw fields should either parse the message text or wait for a future schema version that promotes them to `result`.

**SBOM extraction mode (`--extract-sbom`):**
- Single `info` message containing the raw CycloneDX JSON document
- Or single `error` message with the failure reason

**Exit codes:**
- `0` — inspection succeeded
- `3` — runtime error (non-Windows host, file not found, MSI open failure, SBOM not present)

**Example:**

```bash
forge inspect installer.msi --json
```

```json
{"version":1,"command":"inspect","exitCode":0,"messages":[{"level":"info","text":"MSI: D:\\out\\Demo.msi"},{"level":"info","text":"Product: Demo"},{"level":"info","text":"Manufacturer: Acme"},{"level":"info","text":"Version: 1.0.0"},{"level":"info","text":"Product Code: {12345678-1234-1234-1234-123456789012}"},{"level":"info","text":"Tables: 24"}]}
```

## forge plan --json

**Purpose:** run the installer pipeline through detect + plan and emit the install plan without performing any installation. Accepts a compiled bundle EXE as input.

**Behaviour:** the command (a) extracts the embedded manifest from the bundle EXE via `BundleReader.Extract`, (b) writes the manifest to a temp directory, (c) locates `FalkForge.Engine.exe` alongside the CLI binary, (d) launches the engine headless with `--manifest <path> --plan-only [--plan-output <path>]`, (e) reads the plan JSON written by the engine, and (f) renders a package action summary to the console (or to the `--output` file).

**Envelope:** standard. The `result` map is empty. The plan content surfaces in `messages` as an `info`-level package action summary. The raw plan JSON is available at the `--output` path for programmatic consumption.

**Representative messages (success):**
- `info` — `Plan produced: <N> package action(s)`
- `info` — `  <packageId>  <action>` — one entry per package in the plan

**Representative messages (failure):**
- `error` — `File not found: <path>` — bundle EXE does not exist
- `error` — `Failed to read bundle: <reason>` — bundle is not a valid FALKBUNDLE
- `error` — `Engine binary 'FalkForge.Engine.exe' not found. Ensure the engine is built and placed in the same directory as the forge CLI.`
- `error` — `Engine exited with code <N>.` — engine subprocess failed
- `error` — `Engine did not produce a plan file.` — engine succeeded but wrote no output

**Exit codes:**
- `0` — plan produced successfully
- `3` — runtime error (file not found, invalid bundle, engine not found, engine failure, IO error)

**Example:**

```bash
forge plan installer.exe --json
```

```json
{"version":1,"command":"plan","exitCode":0,"messages":[{"level":"info","text":"Plan produced: 2 package action(s)"},{"level":"info","text":"  MyApp.msi  Install"},{"level":"info","text":"  prereq.msi  Install"}]}
```

**Engine binary requirement:** `forge plan` requires `FalkForge.Engine.exe` to be built (NativeAOT publish) and co-located with the `forge` CLI binary. If the engine is absent the command exits with code `3` and an actionable error message.

## forge verify --json

**Purpose:** independently verify a shipped artifact. Two modes, selected by whether `--rebuild` is passed:

1. **Rebuild-and-compare** (`.msi` or `.exe`) — rebuilds the source project in reproducible mode and byte-compares the result against the supplied artifact. Identical bytes prove provenance — no trust in the build host required.
2. **Signature-only** (`.msi` only, no `--rebuild`) — checks the MSI's pure-.NET ECDSA integrity signature (`MsiIntegrityVerifier`) without needing the source project at all. A bundle has no signature-only mode yet, so `--rebuild` stays required for `.exe`.

### Rebuild-and-compare mode

**Usage:** `forge verify <artifact.msi|artifact.exe> --rebuild <project> [--source-date-epoch <epoch>] [--json]`

**Behaviour:** the command (a) checks the artifact and the `--rebuild` project both exist, (b) resolves `SOURCE_DATE_EPOCH` from `--source-date-epoch`, else the env var, else the git HEAD commit time (same rules as `forge build --reproducible`), (c) rebuilds the project via `dotnet run --project <project> -- -o <tempdir>` with that epoch pinned, (d) locates the rebuilt artifact of the same extension in the temp dir, (e) byte-compares it against the shipped artifact, and (f) emits a verdict. The temp directory is always cleaned up.

> **The rebuild project must opt into reproducible mode** by calling `package.Reproducible()` (MSI) or `BundleBuilder.Reproducible()` (bundle). Without it, two builds differ in PackageCode/ProductCode and timestamps and will never match; `forge verify` then reports `MISMATCH` rather than silently passing.

**Verdicts:**

| Verdict | Exit | Meaning |
|---------|------|---------|
| `VERIFIED` | `0` | Rebuilt artifact is byte-identical to the shipped artifact. |
| `MISMATCH` | `1` | Bytes differ. The diagnostic reports the signed size delta, the total differing-byte count, the first differing offsets (hex), and — for bundles/signed MSIs — the structural region (`footer` / `TOC` / `payload/manifest/stub`) or signature note that explains it. |
| `REBUILD-FAILED` | `2` | The rebuild process exited non-zero (the project did not build). |
| `SETUP-ERROR` | `3` | The rebuild succeeded (exit 0) but produced no artifact of the expected type — a project/config mismatch, not a build failure. Distinct from `REBUILD-FAILED` so a verdict maps to exactly one exit code. |
| (no verdict) | `3` | IO/setup failure *before* the rebuild ran: artifact or project missing, or epoch unresolved. The envelope carries no `verdict` key in this case. |

**Envelope:** standard. The `result` map carries: `verdict` (always), `expectedSize`/`actualSize` (after a successful rebuild), and on `MISMATCH` also `sizeDelta`, `differingBytes`, `firstDifferingOffset`, and for bundles/signed MSIs `region` (bundles only) and `signed`.

**Signed artifacts (known physics):** FalkForge bundles are ECDSA-signed by default (`manifestSignature` in the embedded manifest); MSIs are signed when `.Integrity()` is configured (`_FalkForgeIntegrity`/`ManifestSignature` table row). ECDSA is non-deterministic, so **an `Integrity()`-only signed bundle or MSI can never byte-match across independent rebuilds.** When `forge verify` detects an in-band signature in the rebuilt artifact, the `MISMATCH` diagnostic says so plainly:
- **Bundle:** points to the `FALKFORGE_NO_SIGN` environment variable (rebuild unsigned to compare), or verify the signed bundle via the detach workflow (`forge bundle detach`) instead.
- **MSI:** points to `forge verify <msi>` with no `--rebuild` (signature-only mode, below) to check the signature directly, to `FALKFORGE_NO_SIGN` to compare unsigned bytes, or — the recommended fix — combining `Reproducible()` with `Integrity()`: that combination writes the signature *only* to a detached `<msi>.sig.json` sidecar (no in-band table row), so the MSI bytes themselves stay deterministic and `--rebuild` verification and signature verification both work.

Authenticode code-signing is orthogonal to either check and is not verified by `forge verify` in either mode.

**Representative messages (success):**
- `info` — `VERIFIED: rebuilt artifact is byte-identical (<N> bytes). The artifact provably came from this source.`

**Representative messages (mismatch):**
- `error` — `MISMATCH: rebuilt artifact differs from the shipped artifact.`
- `info` — size delta, differing-byte count, first differing offsets, and region/signed notes

**Exit codes:**
- `0` — `VERIFIED`
- `1` — `MISMATCH`
- `2` — `REBUILD-FAILED`
- `3` — `SETUP-ERROR` (rebuild produced no artifact) or pre-rebuild setup/IO failure (no verdict)

**Example:**

```bash
forge verify app.msi --rebuild installer.csproj --source-date-epoch 1577836800 --json
```

```json
{"version":1,"command":"verify","exitCode":0,"messages":[{"level":"info","text":"VERIFIED: rebuilt artifact is byte-identical (12,288 bytes). The artifact provably came from this source."}],"result":{"verdict":"VERIFIED","expectedSize":"12288","actualSize":"12288"}}
```

### Signature-only mode (.msi, no --rebuild)

**Usage:** `forge verify <artifact.msi> [--trusted-key <fingerprint>]... [--json]`. `--trusted-key` is rejected together with `--rebuild` (validation error) — it has no effect on the rebuild-and-compare path, so combining them would otherwise silently do nothing.

**Behaviour:** `MsiIntegrityVerifier` (a) locates the pure-.NET ECDSA integrity envelope, preferring the embedded `_FalkForgeIntegrity`/`ManifestSignature` table row and falling back to the detached `<msi>.sig.json` sidecar (capped at 4 MiB — an oversized sidecar is refused and reported as `FAILED`, never silently treated as "nothing to see here") when the table is absent (the normal shape for a `Reproducible()`+`Integrity()` MSI, which carries no in-band table at all — see the note above), (b) cryptographically verifies the envelope — against the `--trusted-key` fingerprint set when supplied (establishing authorship), else consistency-only (tamper-evidence only, not authorship), and (c) re-extracts every embedded cabinet, recomputes each payload file's SHA-256, and binds it to the signed declaration **bidirectionally**: every declared file must be present with a matching hash, AND every actual embedded file must be declared. The second direction matters — without it, an attacker could take a genuinely signed, trusted MSI and add an extra, undeclared payload file without touching anything the signature covers, and get a `VERIFIED` result carrying the real publisher's fingerprint. A payload swapped in, altered, or added after signing (leaving the table/sidecar untouched) is caught by this binding even though the signature itself still verifies against its own, unmodified declaration. Windows-only (requires `msi.dll`); non-Windows exits `3`.

> **Uncovered by this check:** the envelope covers embedded payload FILES only — not other MSI database table content (e.g. `Registry`, `CustomAction`, `Property` rows), and not a hybrid post-quantum (ML-DSA) companion signature if present (classical ECDSA-P256 only is checked here; there is no `--pq-key`/`INT011`-equivalent enforcement for MSI yet, unlike the bundle runtime gate).

**Verdicts:**

| Verdict | Exit | Meaning |
|---------|------|---------|
| `VERIFIED` (authorship verified) | `0` | A trusted key (`--trusted-key`) matched: the signature cryptographically verifies against a pinned publisher key, and the MSI's actual payload exactly matches what was signed (no files missing, added, or altered). Rendered in **green**. |
| `VERIFIED` (tamper-evidence only) | `0` | No `--trusted-key` was supplied: the payload is self-consistent and the signature verifies against its own embedded key, but publisher identity was NOT checked. Rendered in **yellow** with the explicit label `VERIFIED (tamper-evidence only — authorship NOT established; pass --trusted-key to verify publisher)` — deliberately distinct from the authorship-verified label so a consistency-only pass is never mistaken for a publisher-authenticated one. |
| `NOT-SIGNED` | `1` | Neither the embedded table nor a detached sidecar carries a signature. Fail-loud: an unsigned MSI is never reported as passing. |
| `FAILED` | `1` | A signature was found but did not verify, matched no trusted key, the MSI's actual payload no longer matches what was signed in either direction (missing, added, or altered files — post-signing tamper), or a located sidecar exceeded the 4 MiB size cap. |
| (no verdict) | `3` | Setup failure — the MSI could not be opened at all. |

**Envelope:** the `result` map carries `verdict` (always — `"VERIFIED"` for both rows above; use `authorshipEstablished` to tell them apart programmatically), `authorshipEstablished` (`"true"`/`"false"`, always present on every signature-only verdict — not just `VERIFIED` — so a consumer never has to infer it from field absence), and, whenever the envelope was located regardless of verdict, `formatTag` (e.g. `falkforge-ecdsa-envelope-v2`; absent when the signature came from a sidecar, which carries no format column, or when nothing was located at all). `fingerprint` is present only when a trusted key matched (implies `authorshipEstablished:"true"`).

**Example (authorship verified):**

```bash
forge verify app.msi --trusted-key A1B2C3D4E5F6...
```

```json
{"version":1,"command":"verify","exitCode":0,"messages":[{"level":"info","text":"VERIFIED (authorship verified): The MSI's embedded payload files exactly match what was signed (no files missing, added, or altered)."}],"result":{"verdict":"VERIFIED","authorshipEstablished":"true","formatTag":"falkforge-ecdsa-envelope-v2","fingerprint":"A1B2C3D4E5F6..."}}
```

**Example (consistency-only, no --trusted-key):**

```bash
forge verify app.msi
```

```json
{"version":1,"command":"verify","exitCode":0,"messages":[{"level":"info","text":"VERIFIED (tamper-evidence only — authorship NOT established; pass --trusted-key to verify publisher): The MSI's embedded payload files exactly match what was signed (no files missing, added, or altered)."}],"result":{"verdict":"VERIFIED","authorshipEstablished":"false","formatTag":"falkforge-ecdsa-envelope-v2"}}
```

## Exit Code Reference

Defined in [`src/FalkForge.Cli/ExitCodes.cs`](../src/FalkForge.Cli/ExitCodes.cs). Mapping is `ErrorKind` → exit code via `ExitCodes.FromErrorKind`.

| Code | Constant | Meaning | `ErrorKind` mapping |
|------|----------|---------|---------------------|
| `0` | `Success` | Command completed without error. | (success) |
| `1` | `ValidationFailure` | Domain validation or configuration error. | `Validation`, `InvalidConfiguration` |
| `2` | `CompilationError` | MSI/bundle compilation failed. | `CompilationError` |
| `3` | `RuntimeError` | Runtime failure (IO, security, platform, missing file, unsupported operation). | `FileNotFound`, `DirectoryNotFound`, `IoError`, `SecurityError`, `PlatformError`, `InvalidOperation`, `NotSupported`, and any unmapped `ErrorKind` |

## Stability

- The envelope schema is versioned via the top-level `version` field. Version `1` is the current contract.
- New optional fields may be added under existing objects without bumping `version`. Consumers must ignore unknown fields.
- Existing fields will not be renamed, retyped, or removed without a major version bump (`version` increment).
- The `result` map is reserved for command-specific structured data. New keys may appear over time; consumers should treat unknown keys as opaque.
- Per-command `data` shapes are not yet first-class fields on the envelope. Command-specific structured data (e.g. inspection results, plan package list) currently surfaces as text in `messages`. When promoted to typed fields they will appear under `result` or a new typed field, with a `version` bump if the change is breaking.

## forge rules list --json

**Purpose:** list all validation rules in the rule catalog for a given target model type, with optional filters.

`forge rules list --json` does **not** use the common envelope. It writes a raw JSON array directly to stdout — one element per matching rule. This makes it easy to pipe into `jq` or other JSON tools without unwrapping an envelope first.

**Supported flags:**
- `--target <package|merge|patch|transform>` — catalog to query (default: `package`)
- `--section <name>` — filter by `ModelSection` enum name (case-insensitive, e.g. `Service`, `Registry`)
- `--severity <error|warning|info>` — filter by severity (case-insensitive)
- `--prefix <PREFIX>` — filter by rule ID prefix (e.g. `PKG`, `SVC`)
- `--json` — emit the array (required for machine-readable output; default is table view)

**Array element shape:**

```json
[
  {
    "id": "PKG001",
    "severity": "Error",
    "section": "Package",
    "title": "Name required",
    "description": "Package Name must not be null, empty, or whitespace-only."
  }
]
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Rule identifier (e.g. `PKG001`, `SVC003`). |
| `severity` | string | One of `Info`, `Warning`, `Error`. |
| `section` | string | `ModelSection` enum name (e.g. `Package`, `Service`, `Registry`). |
| `title` | string | Short human-readable rule title. |
| `description` | string | Full description of what the rule checks and why. |

**Exit codes:**
- `0` — success (even when the filtered result set is empty)
- `1` — unknown `--section` value provided

**Example:**

```bash
forge rules list --json | jq '.[] | select(.severity == "Error") | .id'
forge rules list --target patch --json
forge rules list --section Service --json
```

Source of truth: [`src/FalkForge.Cli/Commands/RulesListCommand.cs`](../src/FalkForge.Cli/Commands/RulesListCommand.cs).

## forge rules explain

**Purpose:** print full metadata for a single validation rule by ID (case-insensitive). Searches all target catalogs.

`forge rules explain` produces human-readable output only (no `--json` flag). Use `forge rules list --json` and filter by ID for machine-readable rule metadata.

**Exit codes:**
- `0` — rule found and printed
- `1` — rule ID not found in any catalog

**Example:**

```bash
forge rules explain PKG001
forge rules explain svc003
```

## Implementation Notes for Consumers

- Always parse the **last** valid JSON object on stdout. The envelope is the only document written by `JsonConsoleOutput.WriteEnvelope`; any preceding output indicates either the command bypassed `--json` mode (bug — please report) or upstream tooling injected text.
- The envelope is single-line (no indentation). Use a streaming JSON parser if you anticipate very large `messages` arrays.
- Spectre markup is stripped, but inner brackets in user-supplied paths or identifiers are preserved verbatim. Treat `text` as opaque human-readable content; do not regex-parse it for structured data — use `level` and `exitCode` instead.
- `command` is the canonical machine-readable identifier. Switch on it rather than on the invoked CLI verb (which a future alias system might shadow).
