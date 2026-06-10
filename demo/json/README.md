# JSON Demo Configurations

Declarative JSON installer definitions built and validated by the `forge` CLI. Use these when you need a standard MSI without custom C# code.

## Overview

JSON configs cover a subset of the FalkForge fluent API: files, shortcuts, registry, services, environment variables, features, major upgrade, downgrade, launch conditions, license, and the Firewall, IIS, SQL, and .NET extensions. For anything beyond this subset — custom actions, file operations, sequence scheduling, custom tables — use the C# fluent API demos instead.

## The 7 Configurations

| File | Dialog Set | Key Features |
|------|-----------|-------------|
| `01-minimal.json` | Minimal | Single file, minimal UI |
| `02-installdir.json` | InstallDir | Desktop shortcut, registry, downgrade block |
| `03-featuretree.json` | FeatureTree | Nested features, services, launch conditions |
| `04-mondo.json` | Mondo | License, environment variables, services, downgrade |
| `05-advanced.json` | Advanced | Nested service features, env vars, all feature types |
| `06-web-server.json` | InstallDir | IIS app pool + web site, firewall rules |
| `07-database-app.json` | InstallDir | SQL database + scripts, .NET runtime detection |

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

The JSON format is documented in [`docs/cli-json-schema.md`](../../docs/cli-json-schema.md). The top-level keys are:

| Key | Type | Description |
|-----|------|-------------|
| `product` | object | Name, manufacturer, version, upgradeCode, platform |
| `ui` | string | Dialog set: `Minimal`, `InstallDir`, `FeatureTree`, `Mondo`, `Advanced` |
| `license` | string | Path to RTF license file (required for Mondo/Advanced) |
| `installDirectory` | string | Default install path relative to `%ProgramFiles%` |
| `majorUpgrade` | object | `schedule` field (e.g., `AfterInstallValidate`) |
| `downgrade` | object | `allow` (bool) and `message` (string) for downgrade behavior |
| `launchConditions` | array | `condition` + `message` pairs |
| `features` | array | Feature tree: `id`, `title`, `files`, `shortcuts`, `registry`, `services`, `environmentVariables`, nested `features` |
| `extensions` | object | `firewall`, `iis`, `sql`, `dotnet` sub-objects |

## Key Differences from the C# API

- Major upgrade schedule lives in `"majorUpgrade": { "schedule": "..." }`.
- Downgrade settings live in a separate top-level `"downgrade": { "allow": false, "message": "..." }` object — they are **not** nested inside `majorUpgrade`.
- Extension configuration (IIS, SQL, Firewall, .NET) is placed in the top-level `"extensions"` object.
