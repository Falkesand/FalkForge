# FalkForge Installer Runtime CLI

This document describes the command-line interface of a FalkForge-built installer — the bundle EXE that end users run on a target machine. It complements [`cli-json-schema.md`](cli-json-schema.md), which documents the `forge` *build-time* CLI used by developers and CI pipelines to produce these installers.

> **Audience:** end users, system administrators, deployment engineers, and support staff who need to control or troubleshoot a FalkForge installer at install time.

Source of truth:

- `src/FalkForge.Engine/Program.cs` — top-level argument parser.
- `src/FalkForge.Engine.Protocol/ProgramArgs.cs` — shared logging-flag parser used by the engine and the UI child process.
- `src/FalkForge.Engine/Bootstrapper.cs` — bundle bootstrapper that forwards flags to the UI child.
- `src/FalkForge.Ui/InstallerApp.cs` — UI side that re-launches the engine for the user-facing process.

## Synopsis

```text
installer.exe                                        # interactive UI (default)
installer.exe --log <path>                           # log to a specific file
installer.exe --log-level <level>                    # set minimum log level
installer.exe --extract-list                         # list embedded payloads
installer.exe --extract <dir> [--package <id>]...    # extract embedded payloads
installer.exe --plan-only [--plan-output <path>]     # dry-run plan (see notes)
installer.exe --sbom <path>                          # extract embedded SBOM
```

Aliases for the logging flags (Windows-style switches are accepted):

```text
installer.exe /log <path>           # equivalent to --log <path>
installer.exe /L   <path>           # equivalent to --log <path>
installer.exe /lv  <level>          # equivalent to --log-level <level>
```

Flags are case-sensitive (the parser switches on exact matches), with the sole exception of the **value** for `--log-level`, which is parsed case-insensitively against the `LogLevel` enum.

## Logging

| Flag | Aliases | Argument | Description |
|------|---------|----------|-------------|
| `--log` | `/log`, `/L` | `<path>` | Path to a log file. If `<path>` resolves to an existing directory, the engine appends `engine.log` and writes there. |
| `--log-level` | `/lv` | `<level>` | Minimum level to record. One of `Verbose`, `Debug`, `Info`, `Warning`, `Error` (case-insensitive). |

**Default log location.** When `--log` is not supplied, the engine writes to a per-session subdirectory under `%TEMP%`:

```text
%TEMP%\FalkForge\<sessionId>\install_<timestamp>.log
```

`<sessionId>` is a randomly generated identifier, which prevents predictable log paths from being abused for symlink or hard-link attacks.

**Path traversal protection.** Paths whose segments contain `..` are rejected before any normalisation:

```text
> installer.exe --log "C:\Logs\..\..\Windows\foo.log"
Error: Log path 'C:\Logs\..\..\Windows\foo.log' contains '..' segments which are not permitted.
```

The engine does **not** silently collapse such paths via `Path.GetFullPath` — only paths the user actually intended are accepted.

**Forwarding.** When the bootstrapper EXE launches the UI child process, and when the UI in turn launches the engine child process, the same `--log` / `--log-level` values are forwarded verbatim. Quoting is handled by the shared `ProgramArgs.ToLogFlagsCommandLine()` so that paths containing whitespace round-trip safely on every hop.

### Examples

```text
installer.exe --log C:\Logs\acme.log --log-level Debug
installer.exe /L C:\Logs --log-level Verbose
installer.exe --log "C:\Program Files\Acme\install.log"
```

If the supplied `--log` value points at a directory, e.g. `C:\Logs`, the engine writes to `C:\Logs\engine.log`.

If parsing fails (missing value or unknown level) the process prints `Error: <reason>` to stderr and exits with code `1`.

## Self-extraction

A FalkForge bundle EXE can list and extract its embedded payloads without running the installer UI. This is useful for IT admins who want to redeploy individual MSIs through their own tooling, or for support staff inspecting what a bundle would install.

| Flag | Argument | Behaviour |
|------|----------|-----------|
| `--extract-list` | *(none)* | Lists every payload in the bundle along with its uncompressed size, then exits. |
| `--extract` | `<dir>` | Extracts payloads into `<dir>`, creating one subdirectory per package id, each containing a `<packageId>.dat` file with the payload bytes. Creates `<dir>` if it does not exist. |
| `--package` | `<id>` | Restricts `--extract` to a specific package id. Repeatable. Without `--package`, all payloads are extracted. |

Examples:

```text
installer.exe --extract-list
Packages in installer.exe:
  AcmeCore                  4.2 MB
  AcmePlugins               1.7 MB
  ...

installer.exe --extract C:\Temp\unpacked
installer.exe --extract C:\Temp\unpacked --package AcmeCore --package AcmePlugins
```

Errors:

- If the requested package ids do not exist in the bundle, the engine prints the missing ids and the available list to stderr and exits with code `1`.
- If the bundle path cannot be resolved (`Environment.ProcessPath` returns null) the engine exits with code `3`.
- If a payload fails to extract (e.g. checksum mismatch) the engine exits with code `2`.

Self-extraction does **not** require elevation and never starts the engine state machine.

## SBOM extraction

| Flag | Argument | Behaviour |
|------|----------|-----------|
| `--sbom` | `<path>` | If the installer manifest carries an embedded SBOM attestation, writes it to `<path>` and exits with code `0`. If no SBOM is available, prints `No SBOM available in this installer.` to stderr and exits with code `1`. |

`--sbom` requires `--manifest` to be set on the same invocation, since the manifest is the carrier of the SBOM string. End users typically do not invoke this directly — it is most useful for compliance tooling that runs against an extracted manifest.

## Plan-only / dry-run

| Flag | Argument | Behaviour |
|------|----------|-----------|
| `--plan-only` | *(none)* | **Currently a no-op.** The flag is parsed but not yet wired through the engine pipeline. See note below. |
| `--plan-output` | `<path>` | **Currently a no-op.** Reserved for the JSON dry-run output once `--plan-only` is wired. |

**Implementation status.** As of the current code (`src/FalkForge.Engine/Program.cs`), both flags are parsed and then explicitly discarded with `_ = planOnly; _ = planOutputPath;`. The engine continues to run the normal install pipeline. Do not rely on these flags to produce a dry-run today; they are placeholders for a future feature.

## Internal flags (do not pass directly)

The following flags are wired internally by the bootstrapper when it spawns the UI child, and by the UI when it spawns the engine child. **End users should not pass them directly** — they will not produce a useful result.

| Flag | Argument | Purpose |
|------|----------|---------|
| `--manifest` | `<path>` | Path to the manifest JSON written by the bootstrapper into a per-invocation cache directory. |
| `--pipe` | `<name>` | Duplex named pipe used for engine ↔ UI communication. |
| `--secret-pipe` | `<name>` | One-shot init pipe used to deliver the HMAC shared secret out-of-band (so the secret never appears on the command line, in process listings, or in event logs). |

`--manifest`, `--pipe`, and `--secret-pipe` are always emitted as a trio. The bootstrapper generates the pipe names and the secret per invocation.

### Deprecated

| Flag | Status |
|------|--------|
| `--secret <value>` | **Deprecated and silently ignored.** Accepted for backward compatibility only — the value is consumed and discarded. The engine now uses the init-pipe pattern (see `--secret-pipe`) to receive secrets, because command-line arguments are visible in process listings and event logs. New code paths must not pass `--secret`. |

## Exit codes

The engine maps its terminal `EngineSession` state to a process exit code:

| Code | `EngineTerminalState` | Meaning |
|------|-----------------------|---------|
| `0` | `Completed` | Install completed successfully. Also returned by successful `--extract-list`, `--extract`, and `--sbom`. |
| `1` | `Failed` (or argument error / SBOM missing / package not found) | Install failed, or a flag was malformed, or the requested resource was missing. |
| `2` | `Cancelled` | The user cancelled the install, or `--extract` failed mid-payload. |
| `3` | `RolledBack` | A failure occurred and the journal rolled the install back. Also returned if the bundle path cannot be resolved during `--extract` / `--extract-list`. |

Any other terminal state currently maps to `1`.

## Precedence and forwarding rules

1. **End-user flags win.** When you invoke the bundle EXE with `installer.exe --log foo.log`, the bootstrapper parses these flags itself, then forwards them to the UI child via `Bootstrapper.BuildUiArgs`. The UI in turn forwards them to the engine child via `InstallerApp.BuildEngineArgs`. There is no override at any hop.
2. **Quoting is centralised.** Both forwarders delegate to `ProgramArgs.ToLogFlagsCommandLine()`. This guarantees that a path with spaces is quoted identically by every process and parsed identically by the receiver.
3. **Bad log flags are non-fatal in the UI.** If the UI fails to parse log flags (e.g. an unknown level), it falls back to no logging rather than refusing to start. The engine, by contrast, exits with code `1` on a bad log flag because it is the authoritative parser.
4. **Self-extraction short-circuits everything else.** When `--extract-list` or `--extract` is present, the engine exits before the bootstrapper, manifest load, pipe setup, or platform check runs. Combining `--extract` with installer flags is therefore effectively undefined — extract-mode wins.

## See also

- [`cli-json-schema.md`](cli-json-schema.md) — the `forge` build-time CLI used by developers and CI to produce these installers.
- [`pipeline-ports.md`](pipeline-ports.md) — engine pipeline architecture, ports, and adapters.
