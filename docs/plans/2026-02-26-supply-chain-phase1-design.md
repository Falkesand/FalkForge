# Supply Chain Security — Phase 1: SBOM, WinGet Manifest, Dry-Run

**Date:** 2026-02-26
**Status:** Design
**Scope:** Three low-effort, high-value supply chain features: CycloneDX SBOM generation, WinGet manifest generation, and dual-mode dry-run planning (headless + GUI), including extension dry-run capability protocol.

---

## 1. SBOM Generation

**Goal:** Emit a CycloneDX 1.6 JSON SBOM as a compilation artifact alongside MSI/Bundle output.

### Rationale

US Executive Order 14028 and the EU Cyber Resilience Act are making SBOMs a regulatory requirement. No installer framework generates SBOMs today. FalkForge already resolves every file with name, version, and SHA-256 during compilation — SBOM emission is a near-free side-effect.

### New Types — `FalkForge.Core/Sbom/`

| File | Type | Description |
|------|------|-------------|
| `ISbomGenerator.cs` | `interface ISbomGenerator` | `Result<string> Generate(SbomDocument, Stream)` |
| `SbomDocument.cs` | `sealed record SbomDocument` | SerialNumber, Metadata, Components, Dependencies |
| `SbomMetadata.cs` | `sealed record SbomMetadata` | Name, Version, Manufacturer, Timestamp |
| `SbomComponent.cs` | `sealed record SbomComponent` | Name, Version, Type, Sha256Hash, Publisher? |
| `SbomComponentType.cs` | `enum SbomComponentType` | File, Library, Application, Framework |
| `SbomDependency.cs` | `sealed record SbomDependency` | Ref, DependsOn |
| `CycloneDxSbomGenerator.cs` | `sealed class CycloneDxSbomGenerator : ISbomGenerator` | Manual CycloneDX 1.6 JSON via `Utf8JsonWriter` (AOT-safe, no reflection) |
| `SbomWriter.cs` | `static class SbomWriter` | `WriteToFile(SbomDocument, string)`, `WriteToString(SbomDocument)` |
| `SbomOptions.cs` | `sealed class SbomOptions` | Fluent options bag for user-supplied components |

No external NuGet dependency — CycloneDX 1.6 JSON is simple enough to write manually.

### Fluent API

```csharp
// src/FalkForge.Core/Builders/PackageBuilder.cs and BundleBuilder.cs
public PackageBuilder Sbom(Action<SbomOptions> configure)
public BundleBuilder Sbom(Action<SbomOptions> configure)

// Usage:
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .Sbom(s => s
        .AddComponent("OpenSSL", "3.2.1", SbomComponentType.Library, sha256: "abc123..."))
    .Feature("Main", f => f.File("MyApp.exe")));
```

### Integration Points

- **`MsiCompiler.Compile()`** — after successful compile, iterate `ResolvedPackage.Files` (already has name + SHA-256), build `SbomDocument`, write `{outputPath}.cdx.json`
- **`BundleCompiler.Compile()`** — iterate `BundleModel.Packages` and payloads, build `SbomDocument`, write `{outputPath}.cdx.json`; bundle SBOM lists each nested MSI SBOM as a dependency by serial number
- Opt-in: only runs when `SbomOptions` is configured on the builder, or `--sbom` CLI flag is set

### CLI

```bash
forge build installer.csx --sbom          # Generate SBOM alongside output
```

New `--sbom` flag on `BuildSettings`. If set without fluent API config, generates SBOM from resolved files only (no user-supplied components).

### Error Codes

| Code | Description |
|------|-------------|
| SBM001 | Failed to compute SHA-256 hash for SBOM component |
| SBM002 | Failed to write SBOM output file |

---

## 2. WinGet Manifest Generation

**Goal:** Auto-generate a WinGet installer YAML manifest from compiled output metadata.

### Rationale

Publishing to WinGet requires hand-authoring YAML. FalkForge already has all the metadata — automatic generation removes friction from the distribution story.

### New Types — `FalkForge.Cli/WinGet/`

| File | Type | Description |
|------|------|-------------|
| `WinGetManifestGenerator.cs` | `static class WinGetManifestGenerator` | `Result<Unit> Generate(string outputFilePath, WinGetManifestOptions, string destinationPath)` |
| `WinGetManifestOptions.cs` | `sealed record WinGetManifestOptions` | All WinGet manifest fields |

No external YAML library — WinGet singleton manifests are simple key-value YAML written manually.

### Field Mapping

| WinGet Field | Source |
|---|---|
| `PackageIdentifier` | `{Manufacturer}.{ProductName}` sanitized to `[a-zA-Z0-9.-]` |
| `PackageVersion` | `PackageModel.Version` |
| `InstallerType` | `msi` for MSI, `burn` for bundle |
| `InstallerSha256` | SHA-256 of compiled output file |
| `Architecture` | `PackageModel.Platform` (x86/x64/arm64) |
| `Scope` | `PackageModel.InstallScope` (machine/user) |
| `ProductCode` | `PackageModel.ProductCode` |
| `InstallerUrl` | Required — must be supplied via fluent API or `--winget-url` |

### Fluent API

```csharp
// src/FalkForge.Core/Builders/PackageBuilder.cs and BundleBuilder.cs
public PackageBuilder WinGet(Action<WinGetOptions> configure)
public BundleBuilder WinGet(Action<WinGetOptions> configure)

// Usage:
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .WinGet(w => w
        .InstallerUrl("https://releases.contoso.com/v{version}/setup.msi")
        .License("MIT")
        .Description("A productivity tool for developers")
        .Tags("developer-tools", "productivity"))
    .Feature("Main", f => f.File("MyApp.exe")));
```

### Output

```yaml
# Generated by FalkForge
# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: Contoso.MyApp
PackageVersion: 1.0.0
Platform:
  - Windows.Desktop
MinimumOSVersion: 10.0.0.0
InstallerType: msi
Scope: machine
InstallModes:
  - interactive
  - silent
  - silentWithProgress
InstallerSwitches:
  Silent: /quiet /norestart
  SilentWithProgress: /passive /norestart
Installers:
  - Architecture: x64
    InstallerUrl: https://releases.contoso.com/v1.0.0/setup.msi
    InstallerSha256: A1B2C3D4...
    ProductCode: "{12345678-1234-1234-1234-123456789ABC}"
ManifestType: installer
ManifestVersion: 1.6.0
```

Output file: `{outputName}.winget.yaml` alongside the MSI/EXE.

### CLI

```bash
forge build installer.csx --winget                          # Uses InstallerUrl from fluent API
forge build installer.csx --winget --winget-url "https://..." # Override URL
```

### Error Codes

| Code | Description |
|------|-------------|
| WGT001 | PackageIdentifier could not be derived (missing manufacturer or product name) |
| WGT002 | Failed to compute SHA-256 of output file |
| WGT003 | Failed to write WinGet manifest file |

---

## 3. Dry-Run Planning Mode

**Goal:** Two complementary dry-run modes — headless (CI/CD) and GUI (testing/demo) — with a protocol for extensions to declare dry-run support.

### 3A. Headless Plan Export (`forge plan`)

Compiles the installer, runs the engine with `--plan-only`, emits JSON to stdout or a file. No UI. For CI/CD pipelines and change management approval workflows.

**Engine changes (`FalkForge.Engine`):**

- `EngineHost` detects `--plan-only` argument
- State machine runs `Initializing → Detecting → Planning → Shutdown` only — skips Elevating and Applying
- New `PlanExporter` static class serializes `InstallPlan` to JSON
- New `PlanJsonContext` (AOT `JsonSerializerContext`) for NativeAOT-safe serialization
- New output records: `PlanOutput`, `PlanPackageOutput`, `PlanFeatureOutput`

**Output format:**

```json
{
  "action": "install",
  "packages": [
    {
      "id": "MyApp.msi",
      "type": "MsiPackage",
      "action": "install",
      "version": "1.0.0",
      "properties": { "INSTALLFOLDER": "C:\\Program Files\\MyApp" }
    }
  ],
  "features": [
    { "id": "MainFeature", "action": "install" },
    { "id": "OptionalDocs", "action": "absent" }
  ],
  "extensionActions": [
    { "description": "Add URL reservation http://+:8080/ for Network Service", "kind": "Configure" },
    { "description": "Add firewall rule: MyApp HTTP inbound TCP 8080", "kind": "Configure" }
  ],
  "estimatedDiskUsage": "45 MB",
  "requiresElevation": true,
  "requiresReboot": false
}
```

**CLI command (`FalkForge.Cli`):** new `PlanCommand` + `PlanSettings`. Compiles the installer then launches the engine exe with `--plan-only`. Supports `-o plan.json` to write output to file.

```bash
forge plan installer.csx              # JSON to stdout
forge plan installer.csx -o plan.json # Write to file
forge plan installer.csx | jq '.packages'  # Pipe-friendly
```

### 3B. GUI Dry-Run (`--dry-run`)

The bundle EXE accepts a `--dry-run` flag passed through to the engine. The full UI launches normally. The user clicks through every page, makes all selections, hits Install — but the engine simulates the Apply phase instead of executing it.

**Engine changes:**
- `EngineHost` detects `--dry-run` flag, sets `EngineContext.IsDryRun = true`
- `PackageExecutor.ExecuteAsync()` checks `IsDryRun` → logs what it would do instead of calling `MsiExecutor`/`ExeExecutor`/etc.
- Progress messages are still sent to the UI (realistic progress animation)
- On completion, engine writes `{ProductName}-dry-run-{timestamp}.json` to `%TEMP%`
- `UpdateAvailableMessage` is suppressed in dry-run (no update check side effects)

**UI changes:**
- Persistent banner on every page: "DRY RUN — no changes will be made" (`AutomationProperties.Name` set for screen reader compatibility)
- Complete page shows: "Dry run complete — no changes were made" with a link/path to the log file

**Fluent API (optional — for always-dry-run test builds):**
```csharp
// src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs
public BundleBuilder DryRun()  // Bakes --dry-run into the manifest; runtime flag takes precedence
```

Runtime use requires no special build — `MyInstaller.exe --dry-run` always works.

### 3C. Extension Dry-Run Capability Protocol

Dry-run mode is **all-or-nothing**: if any registered extension does not declare dry-run support, dry-run mode is blocked entirely with a clear error identifying the unsupported extensions.

**New types in `FalkForge.Extensibility`:**

```csharp
// src/FalkForge.Extensibility/IDryRunContributor.cs
public interface IDryRunContributor
{
    IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent);
}

// src/FalkForge.Extensibility/DryRunAction.cs
public sealed record DryRunAction(string Description, DryRunActionKind Kind);

// src/FalkForge.Extensibility/DryRunIntent.cs
public enum DryRunIntent { Install, Uninstall }

// src/FalkForge.Extensibility/DryRunActionKind.cs
public enum DryRunActionKind { Configure, Unconfigure }
```

**Registration:**
```csharp
// src/FalkForge.Extensibility/IExtensionRegistry.cs (addition)
void RegisterDryRunContributor(IDryRunContributor contributor);
```

**Compile-time flow:**
- Compiler collects all registered extension names and which ones provided `IDryRunContributor`
- Both lists are embedded in the manifest JSON: `registeredExtensions` and `dryRunActions`

**Runtime flow (engine):**
- On `--dry-run` or `--plan-only` startup, engine reads manifest
- Checks: are there registered extensions with no corresponding dry-run actions?
- If any: fail immediately, list them by name:

```
Error: Dry-run mode is not available.
The following extensions do not support it:
  - MyCustomExtension
  - ThirdPartyExtension

Extension authors: implement IDryRunContributor and register via IExtensionRegistry.RegisterDryRunContributor().
```

- If all extensions support dry-run (or no extensions registered): proceed

**Built-in extensions** — all 7 must implement `IDryRunContributor` as part of this feature:

| Extension | Dry-Run Actions |
|-----------|----------------|
| Http | "Add URL reservation {url} for {user}" / "Add SNI SSL binding {host}:{port}" |
| Firewall | "Add firewall rule: {name} ({protocol} {port} {direction})" |
| IIS | "Create app pool: {name}" / "Create web site: {name}" / "Add binding: {host}:{port}" |
| Sql | "Create database: {name} on {server}" / "Execute script: {name}" |
| Util | "Configure XML: {file}" / "Create user: {name}" / "Create file share: {name}" / etc. |
| Dependency | "Register dependency provider: {key}" |
| DotNet | "Detect .NET {version} (read-only, no system changes)" |

### Error Codes

| Code | Description |
|------|-------------|
| PLN001 | Detection phase failed during plan-only mode |
| PLN002 | Planning phase failed during plan-only mode |
| PLN003 | Failed to serialize plan to JSON |
| PLN004 | Dry-run mode blocked: one or more extensions do not support it |

---

## Testing Strategy

### SBOM

- `CycloneDxSbomGeneratorTests`: valid CycloneDX 1.6 JSON output, all required fields present, component entries match input, serial number is unique UUID
- `MsiCompilerTests` extension: SBOM file emitted alongside MSI when `SbomOptions` configured
- `BundleCompilerTests` extension: bundle SBOM lists MSI SBOM as dependency

### WinGet

- `WinGetManifestGeneratorTests`: valid YAML output, PackageIdentifier sanitization, SHA-256 field present, InstallerUrl token substitution
- Error path: missing manufacturer → WGT001

### Dry-Run

- `PlanExporterTests`: valid JSON, all fields present, extension actions included
- `EngineHostTests`: `--plan-only` exits after Planning phase without entering Elevating
- `EngineHostTests`: `--dry-run` with unsupported extension → PLN004 error, lists extension names
- `PackageExecutorTests`: dry-run mode skips actual MSI execution, returns success
- Extension tests: each built-in extension's `IDryRunContributor` returns correct action descriptions for Install and Uninstall intents
