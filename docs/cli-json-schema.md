# forge --json Output Schema

The `forge` CLI emits a single structured JSON document on stdout when invoked with `--json`. CI pipelines, IDE integrations, and other automation should parse this envelope rather than scraping the human-oriented Spectre.Console rendering.

The `--json` flag is supported on:

| Command | Status |
|---------|--------|
| `forge build` | implemented |
| `forge validate` | implemented |
| `forge inspect` | implemented (Windows-only) |
| `forge plan` | envelope implemented; underlying plan dispatcher pending engine binary integration |

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

**Purpose:** run the installer pipeline through detect + plan and emit the install plan as JSON without performing any installation. The CLI envelope is wired up; the underlying engine dispatch is not yet implemented because it requires the NativeAOT engine binary to be built and located on disk.

**Current behaviour:** the command writes an `error`-level message indicating that the engine binary is required, then emits the standard envelope with `exitCode = 3`.

**Envelope:** standard. The `result` map is empty. Once the engine subprocess is wired in (`PlanCommand.BuildEngineArgs` is the entry point), the plan payload — currently produced by the engine via `--plan-only` and `--plan-output` — will be promoted into a structured field. The exact shape will be added to this document at that time; today no plan steps appear in the JSON output.

**Representative messages (current placeholder):**
- `error` — `The 'forge plan' command requires the engine binary to be compiled first.`
- `error` — `Project: <path>`

**Exit codes:**
- `3` — runtime error (file not found, or engine binary not yet integrated — current default)
- `0` — reserved for successful plan emission once engine integration lands

**Example (current placeholder output):**

```bash
forge plan installer.csx --json
```

```json
{"version":1,"command":"plan","exitCode":3,"messages":[{"level":"error","text":"The 'forge plan' command requires the engine binary to be compiled first."},{"level":"error","text":"Project: installer.csx"}]}
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
- Per-command `data` shapes are not yet first-class fields on the envelope. They are tracked here as roadmap notes (see `forge plan` and `forge inspect` sections); when promoted, they will appear under `result` (string-valued summary keys) or under a new typed field, with a `version` bump if the change is breaking.

## Implementation Notes for Consumers

- Always parse the **last** valid JSON object on stdout. The envelope is the only document written by `JsonConsoleOutput.WriteEnvelope`; any preceding output indicates either the command bypassed `--json` mode (bug — please report) or upstream tooling injected text.
- The envelope is single-line (no indentation). Use a streaming JSON parser if you anticipate very large `messages` arrays.
- Spectre markup is stripped, but inner brackets in user-supplied paths or identifiers are preserved verbatim. Treat `text` as opaque human-readable content; do not regex-parse it for structured data — use `level` and `exitCode` instead.
- `command` is the canonical machine-readable identifier. Switch on it rather than on the invoked CLI verb (which a future alias system might shadow).
