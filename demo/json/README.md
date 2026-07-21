# JSON Demo Configurations

Declarative JSON installer definitions built and validated by the `forge` CLI. Use these when you need a standard MSI without custom C# code.

> **Work in progress.** The C# fluent API is FalkForge's primary, fully supported authoring
> path (what `forge init` scaffolds). JSON configuration is an experimental subset. Most
> notably, JSON **cannot author the Firewall, IIS, SQL, or .NET-detection extensions** — an
> `extensions` block with any content is a hard build error (JSN019), not a silent no-op.
> `06-web-server.json` and `07-database-app.json` are plain file-only installers; use the C#
> fluent API for any of the four extensions today (see demo 07, "Extensions Showcase", or the
> focused demos 29–32).

## Overview

JSON configs cover a subset of the FalkForge fluent API: files, shortcuts, registry, services, environment variables, features, major upgrade, downgrade, launch conditions, and license. The Firewall, IIS, SQL, and .NET extension sections are structurally recognized and validated (see below), but authoring any of them in JSON fails the build with JSN019 — for anything beyond the working subset — custom actions, file operations, sequence scheduling, custom tables, and the four extensions — use the C# fluent API demos instead.

## The 7 Configurations

| File | Dialog Set | Key Features |
|------|-----------|-------------|
| `01-minimal.json` | Minimal | Single file, minimal UI |
| `02-installdir.json` | InstallDir | Desktop shortcut, registry, downgrade block |
| `03-featuretree.json` | FeatureTree | Nested features, services, launch conditions |
| `04-mondo.json` | Mondo | License, environment variables, services, downgrade |
| `05-advanced.json` | Advanced | Nested service features, env vars, all feature types |
| `06-web-server.json` | InstallDir | Basic file installer (JSON can't author IIS/firewall — see note above) |
| `07-database-app.json` | InstallDir | Basic file installer (JSON can't author SQL/.NET detection — see note above) |

### Payload files

All `payload/` directories contain **dummy placeholder files** (zero-byte or minimal content). They exist so that the configs compile and file references resolve. Replace them with real application binaries when adapting for production use.

## Building and Validating

Validate a JSON config without producing an MSI (checks schema and business rules):

```bash
forge validate demo/json/01-minimal.json
forge validate demo/json/02-installdir.json
forge validate demo/json/03-featuretree.json
forge validate demo/json/04-mondo.json
forge validate demo/json/05-advanced.json
forge validate demo/json/06-web-server.json
forge validate demo/json/07-database-app.json
```

Build an MSI from a JSON config (requires Windows with `msi.dll`):

```bash
forge build demo/json/01-minimal.json -o ./output
forge build demo/json/06-web-server.json -o ./output
```

## JSON Schema

The JSON format is documented in the [JSON Configuration Format](../../documentation.html#cli-json-config) section of the reference docs and the [JSON Configuration tutorial](../../docs/tutorials/json-config.html). (Note: `docs/cli-json-schema.md` documents a different thing — the `--json` machine-readable *output* envelope emitted by `forge` commands, not this installer-definition input format.) The top-level keys are:

| Key | Type | Description |
|-----|------|-------------|
| `product` | object | Name, manufacturer, version, upgradeCode, platform |
| `ui` | string | Dialog set: `Minimal`, `InstallDir`, `FeatureTree`, `Mondo`, `Advanced` |
| `license` | string | Path to RTF license file (required for Mondo/Advanced) |
| `installDirectory` | string | Default install path relative to `%ProgramFiles%` |
| `majorUpgrade` | object | `schedule` field (e.g., `AfterInstallValidate`) |
| `downgrade` | object | `allow` (bool) and `message` (string) for downgrade behavior |
| `launchConditions` | array | `condition` + `message` pairs |
| `features` | array | Feature tree: `id`, `title`, `files` (each with an optional nested `shortcut` object), `registry`, `services`, `environmentVariables`, nested `features` |
| `extensions` | object | `firewall`, `iis`, `sql`, `dotnet` sub-objects — structurally validated, but any non-empty content fails the build (JSN019); not usable to actually author extensions |

## Key Differences from the C# API

- Major upgrade schedule lives in `"majorUpgrade": { "schedule": "..." }`.
- Downgrade settings live in a separate top-level `"downgrade": { "allow": false, "message": "..." }` object — they are **not** nested inside `majorUpgrade`.
- Extension configuration (IIS, SQL, Firewall, .NET) has a place in the top-level `"extensions"` object and is structurally validated the same way as a C# build — but, unlike the C# API, JSON cannot actually author those extensions: any non-empty `extensions` content fails the build with JSN019 rather than producing an installer that silently lacks them. Use the C# fluent API for Firewall, IIS, SQL, or .NET-detection configuration.
