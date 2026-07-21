# JSON Demo Configurations

Declarative JSON installer definitions built and validated by the `forge` CLI. Use these when you need a standard MSI without custom C# code.

> **Work in progress.** The C# fluent API is FalkForge's primary, fully supported authoring
> path (what `forge init` scaffolds). JSON configuration is an experimental subset. JSON **can**
> now author the Firewall, IIS, and SQL extensions — an `extensions` block is translated into the
> same real extensions the C# API attaches via `new MsiCompiler().Use(...)` and emitted into the
> compiled MSI (`06-web-server.json` shows firewall + IIS, `07-database-app.json` shows SQL). The
> one exception is **.NET runtime detection**, a bundle-engine feature with no standalone-MSI
> representation: a `dotnet` block still fails the build with JSN019. Use the C# fluent API for
> .NET detection (demo 32) and for anything beyond the JSON subset.

## Overview

JSON configs cover a subset of the FalkForge fluent API: files, shortcuts, registry, services, environment variables, features, major upgrade, downgrade, launch conditions, license, and the Firewall / IIS / SQL extensions. The `dotnet` extension section is structurally recognized and validated but cannot be authored in JSON (JSN019). For anything beyond that subset — custom actions, file operations, sequence scheduling, custom tables, and .NET detection — use the C# fluent API demos instead.

## The 7 Configurations

| File | Dialog Set | Key Features |
|------|-----------|-------------|
| `01-minimal.json` | Minimal | Single file, minimal UI |
| `02-installdir.json` | InstallDir | Desktop shortcut, registry, downgrade block |
| `03-featuretree.json` | FeatureTree | Nested features, services, launch conditions |
| `04-mondo.json` | Mondo | License, environment variables, services, downgrade |
| `05-advanced.json` | Advanced | Nested service features, env vars, all feature types |
| `06-web-server.json` | InstallDir | Web app with firewall rule + IIS app pool & web site (JSON `extensions`) |
| `07-database-app.json` | InstallDir | Database app with SQL database + install script (JSON `extensions`) |

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
| `extensions` | object | `firewall`, `iis`, `sql` sub-objects are translated into the real extensions and emitted into the MSI; the `dotnet` sub-object is structurally validated but not buildable in JSON (JSN019) |

## Key Differences from the C# API

- Major upgrade schedule lives in `"majorUpgrade": { "schedule": "..." }`.
- Downgrade settings live in a separate top-level `"downgrade": { "allow": false, "message": "..." }` object — they are **not** nested inside `majorUpgrade`.
- Extension configuration lives in the top-level `"extensions"` object. The `firewall`, `iis`, and `sql` sections are translated into the same real extensions the C# API attaches via `new MsiCompiler().Use(...)`, so a JSON build emits the identical firewall / IIS / SQL tables into the MSI. The `dotnet` (.NET runtime detection) section is the exception: it is a bundle-engine feature with no standalone-MSI representation, so a `dotnet` block fails the build with JSN019 (rather than silently producing an installer that does not gate on the runtime). Use the C# fluent API for .NET-detection configuration (demo 32).
