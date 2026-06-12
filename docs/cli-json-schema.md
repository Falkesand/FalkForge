# forge --json Output Schema

The `forge` CLI emits a single structured JSON document on stdout when invoked with `--json`. CI pipelines, IDE integrations, and other automation should parse this envelope rather than scraping the human-oriented Spectre.Console rendering.

The `--json` flag is supported on:

| Command | Status |
|---------|--------|
| `forge build` | implemented |
| `forge validate` | implemented |
| `forge inspect` | implemented (Windows-only) |
| `forge plan` | implemented — extracts bundle manifest, launches engine headless in plan-only mode, renders package action summary |
| `forge verify` | implemented — rebuilds the source project reproducibly and byte-compares against a shipped artifact; emits a VERIFIED/MISMATCH/REBUILD-FAILED verdict |
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
- `info` — `WinGet manifest written alongside installer` (when `--winget`)
- `warning` — `WinGet manifest generation failed: <reason>`
- `error` — script load / compilation failure messages, surfaced from `Result<T>.Error.Message`

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

**Purpose:** independently verify that a shipped artifact came from a given source. Rebuilds the source project in reproducible mode and byte-compares the result against the supplied artifact. Identical bytes prove provenance — no trust in the build host required.

**Usage:** `forge verify <artifact.msi|artifact.exe> --rebuild <project> [--source-date-epoch <epoch>] [--json]`

**Behaviour:** the command (a) checks the artifact and the `--rebuild` project both exist, (b) resolves `SOURCE_DATE_EPOCH` from `--source-date-epoch`, else the env var, else the git HEAD commit time (same rules as `forge build --reproducible`), (c) rebuilds the project via `dotnet run --project <project> -- -o <tempdir>` with that epoch pinned, (d) locates the rebuilt artifact of the same extension in the temp dir, (e) byte-compares it against the shipped artifact, and (f) emits a verdict. The temp directory is always cleaned up.

> **The rebuild project must opt into reproducible mode** by calling `package.Reproducible()` (MSI) or `BundleBuilder.Reproducible()` (bundle). Without it, two builds differ in PackageCode/ProductCode and timestamps and will never match; `forge verify` then reports `MISMATCH` rather than silently passing.

**Verdicts:**

| Verdict | Exit | Meaning |
|---------|------|---------|
| `VERIFIED` | `0` | Rebuilt artifact is byte-identical to the shipped artifact. |
| `MISMATCH` | `1` | Bytes differ. The diagnostic reports the signed size delta, the total differing-byte count, the first differing offsets (hex), and — for bundles — the structural region (`footer` / `TOC` / `payload/manifest/stub`) the first difference falls in. |
| `REBUILD-FAILED` | `2` | The rebuild process exited non-zero (the project did not build). |
| (setup failure) | `3` | Artifact or project missing, epoch unresolved, or the rebuild produced no artifact of the expected type. |

**Envelope:** standard. The `result` map carries: `verdict` (always), `expectedSize`/`actualSize` (after a successful rebuild), and on `MISMATCH` also `sizeDelta`, `differingBytes`, `firstDifferingOffset`, and for bundles `region` and `signed`.

**Signed bundles (known physics):** FalkForge bundles are ECDSA-signed by default (`manifestSignature` in the embedded manifest). ECDSA is non-deterministic, so **a signed bundle can never byte-match across independent rebuilds.** When `forge verify` detects a `manifestSignature` in the rebuilt bundle, the `MISMATCH` diagnostic says so plainly and points to the `FALKFORGE_NO_SIGN` environment variable: rebuild and ship with `FALKFORGE_NO_SIGN` set to make the bundle deterministic and byte-verifiable, or verify the signed bundle's payloads via the detach workflow (`forge bundle detach`) instead. MSI artifacts are unaffected; signed-MSI (Authenticode) verification is not supported and reports a mismatch.

**Representative messages (success):**
- `info` — `VERIFIED: rebuilt artifact is byte-identical (<N> bytes). The artifact provably came from this source.`

**Representative messages (mismatch):**
- `error` — `MISMATCH: rebuilt artifact differs from the shipped artifact.`
- `info` — size delta, differing-byte count, first differing offsets, and region/signed notes

**Exit codes:**
- `0` — `VERIFIED`
- `1` — `MISMATCH`
- `2` — `REBUILD-FAILED`
- `3` — setup/IO failure

**Example:**

```bash
forge verify app.msi --rebuild installer.csproj --source-date-epoch 1577836800 --json
```

```json
{"version":1,"command":"verify","exitCode":0,"messages":[{"level":"info","text":"VERIFIED: rebuilt artifact is byte-identical (12,288 bytes). The artifact provably came from this source."}],"result":{"verdict":"VERIFIED","expectedSize":"12288","actualSize":"12288"}}
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
