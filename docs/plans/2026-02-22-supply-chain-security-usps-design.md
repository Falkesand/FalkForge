# Supply Chain Security & Developer Experience USPs

**Date:** 2026-02-22
**Status:** Design
**Scope:** 7 cross-cutting features differentiating FalkForge from all competitor installer frameworks

## Context

FalkForge is a C# MSI/Bundle installer framework with fluent API, NativeAOT engine, WPF custom UI, and extension system. It compiles to MSI/MSM/MSP/MST/EXE bundle output types. The user has a separate CLI tool called Sigil.Sign (NuGet: `Sigil.Sign`, GitHub: `Falkesand/Sigil`) for cryptographic signing.

No existing installer framework — WiX, InstallShield, Advanced Installer, MSIX, or Inno Setup — offers integrated supply chain security, reproducible builds, policy-as-code governance, or accessibility compliance. These 7 features collectively establish FalkForge's unique value proposition:

> **"The only installer framework with integrated supply chain security."**

## Implementation Priority

| # | Feature | Effort | Value |
|---|---------|--------|-------|
| 1 | SBOM Generation | Low | High visibility, foundation for signing |
| 2 | Sigil.Sign Integration | Medium | Enables signing + attestation + SBOM signing |
| 3 | WinGet Manifest Generation | Low | Distribution story |
| 4 | Dry-Run Planning Mode | Low | Enterprise operations |
| 5 | Policy-as-Code Validation | Medium | Enterprise governance |
| 6 | Reproducible MSI Builds | Medium | Technical differentiator |
| 7 | Accessible Custom UI — WCAG 2.2 AA | Medium | Legal compliance (EU Accessibility Act) |

---

## 1. SBOM Generation

**Effort:** Low
**Goal:** Emit CycloneDX JSON SBOM as a compilation artifact alongside MSI/Bundle output.

### Rationale

Software Bill of Materials is becoming a regulatory requirement (US Executive Order 14028, EU Cyber Resilience Act). No installer framework generates SBOMs today. FalkForge will be first.

### Design

During MSI compilation, the compiler already resolves every file entry with name, version, and SHA-256 hash via `ResolvedPackage`. During Bundle compilation, all packages with type, version, and hash are available from `BundleModel`. This data maps directly to CycloneDX 1.6 component entries.

The SBOM is generated as a post-compilation artifact: `{OutputName}.cdx.json` alongside the MSI/EXE. No external NuGet dependency is required — CycloneDX 1.6 JSON is a simple schema that can be written manually.

### Key Types

New types in `FalkForge.Core`:

```csharp
// src/FalkForge.Core/Sbom/ISbomGenerator.cs
public interface ISbomGenerator
{
    Result<string> Generate(SbomDocument document, Stream output);
}

// src/FalkForge.Core/Sbom/SbomComponent.cs
public sealed record SbomComponent(
    string Name,
    string Version,
    SbomComponentType Type,
    string Sha256Hash,
    string? Publisher = null);

// src/FalkForge.Core/Sbom/SbomComponentType.cs
public enum SbomComponentType
{
    File,
    Library,
    Application,
    Framework
}

// src/FalkForge.Core/Sbom/SbomDocument.cs
public sealed record SbomDocument(
    string SerialNumber,
    SbomMetadata Metadata,
    IReadOnlyList<SbomComponent> Components,
    IReadOnlyList<SbomDependency> Dependencies);

// src/FalkForge.Core/Sbom/SbomMetadata.cs
public sealed record SbomMetadata(
    string Name,
    string Version,
    string Manufacturer,
    DateTimeOffset Timestamp);

// src/FalkForge.Core/Sbom/SbomDependency.cs
public sealed record SbomDependency(
    string Ref,
    IReadOnlyList<string> DependsOn);
```

Implementation:

```csharp
// src/FalkForge.Core/Sbom/CycloneDxSbomGenerator.cs
public sealed class CycloneDxSbomGenerator : ISbomGenerator
{
    private const string BomFormat = "CycloneDX";
    private const string SpecVersion = "1.6";

    public Result<string> Generate(SbomDocument document, Stream output)
    {
        // Writes minimal CycloneDX 1.6 JSON:
        // { "bomFormat", "specVersion", "serialNumber", "metadata", "components", "dependencies" }
        // No System.Text.Json source generator needed — manual UTF-8 writing for AOT safety
    }
}
```

Static writer for convenience:

```csharp
// src/FalkForge.Core/Sbom/SbomWriter.cs
public static class SbomWriter
{
    public static Result<Unit> WriteToFile(SbomDocument document, string filePath);
    public static Result<string> WriteToString(SbomDocument document);
}
```

### Fluent API

Extension point on `PackageBuilder` and `BundleBuilder` for user-supplied component metadata:

```csharp
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .Sbom(s => s
        .AddComponent("OpenSSL", "3.2.1", SbomComponentType.Library, sha256: "abc123...")
        .AddComponent("zlib", "1.3.1", SbomComponentType.Library, sha256: "def456..."))
    .Feature("Main", f => f
        .File("MyApp.exe")));
```

### Integration Points

- `MsiCompiler.Compile()` — after successful compilation, iterate `ResolvedPackage.Files` to collect `SbomComponent` entries, build `SbomDocument`, write to `{outputPath}.cdx.json`
- `BundleCompiler.Compile()` — iterate `BundleModel.Packages` and payloads, build `SbomDocument`, write to `{outputPath}.cdx.json`
- Bundle SBOM includes nested MSI SBOMs as dependency references (by serial number)
- SBOM generation is opt-in: only runs when `SbomOptions` is configured on the builder or `--sbom` CLI flag is set

### CLI

```bash
forge build installer.csx --sbom           # Generate SBOM alongside output
forge build installer.csx --sbom --sign sigil  # Generate + sign SBOM (see section 7)
```

### Error Codes

| Code | Description |
|------|-------------|
| SBM001 | Failed to compute file hash for SBOM component |
| SBM002 | Failed to write SBOM output file |

---

## 2. WinGet Manifest Generation

**Effort:** Low
**Goal:** Auto-generate WinGet `installer.yaml` manifest from PackageModel metadata after compilation.

### Rationale

Publishing to WinGet requires hand-authoring YAML manifests. FalkForge already has all the metadata needed. Automatic generation removes friction from the distribution story.

### Design

A static generator takes the compiled output path and source model, and produces a WinGet singleton manifest. No external YAML library is needed — WinGet manifests are simple key-value YAML that can be written manually.

### Key Types

```csharp
// src/FalkForge.Cli/WinGet/WinGetManifestGenerator.cs
public static class WinGetManifestGenerator
{
    public static Result<Unit> Generate(
        string outputFilePath,
        WinGetManifestOptions options,
        string destinationPath);
}

// src/FalkForge.Cli/WinGet/WinGetManifestOptions.cs
public sealed record WinGetManifestOptions
{
    public required string PackageIdentifier { get; init; }
    public required string PackageVersion { get; init; }
    public required string InstallerType { get; init; }  // "msi" | "burn"
    public required string InstallerSha256 { get; init; }
    public string InstallerUrl { get; init; } = "https://example.com/download/{version}/{filename}";
    public string? Platform { get; init; }               // "x86" | "x64" | "arm64"
    public string? Scope { get; init; }                  // "machine" | "user"
    public string? ProductCode { get; init; }
    public string? InstallerSwitches { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Description { get; init; }
    public string? Publisher { get; init; }
    public string? License { get; init; }
}
```

### Field Mapping

| WinGet Field | Source |
|-------------|--------|
| `PackageIdentifier` | `{Manufacturer}.{ProductName}` sanitized to `[a-zA-Z0-9.-]` |
| `PackageVersion` | `PackageModel.Version` or `BundleModel.Version` |
| `InstallerType` | `msi` for MSI output, `burn` for bundle output |
| `InstallerSha256` | SHA-256 of compiled output file |
| `InstallerUrl` | Placeholder with `{version}` and `{filename}` tokens |
| `Platform` | From `PackageModel.Platform` (x86/x64/arm64) |
| `Scope` | From `PackageModel.InstallScope` (machine/user) |
| `ProductCode` | From `PackageModel.UpgradeCode` |
| `InstallerSwitches` | `/quiet /norestart` defaults |

### Fluent API

```csharp
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .WinGet(w => w
        .PackageIdentifier("Contoso.MyApp")
        .InstallerUrl("https://releases.contoso.com/v{version}/setup.msi")
        .Tags("developer-tools", "productivity")
        .License("MIT")
        .Description("A productivity tool for developers"))
    .Feature("Main", f => f
        .File("MyApp.exe")));
```

### Output Format

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
    InstallerSha256: A1B2C3D4E5F6...
    ProductCode: "{12345678-1234-1234-1234-123456789ABC}"
ManifestType: installer
ManifestVersion: 1.6.0
```

### CLI

```bash
forge build installer.csx --winget                    # Generate manifest with defaults
forge build installer.csx --winget --winget-url "https://..."  # Override URL
```

### Error Codes

| Code | Description |
|------|-------------|
| WGT001 | PackageIdentifier could not be derived (missing manufacturer or product name) |
| WGT002 | Failed to compute SHA-256 of output file |
| WGT003 | Failed to write WinGet manifest file |

---

## 3. Dry-Run Planning Mode

**Effort:** Low
**Goal:** Engine outputs a JSON plan of what would be installed/modified/removed without executing anything.

### Rationale

Enterprise change management processes require a preview of what an installer will do before approval. The engine already has `Detecting -> Planning` phases producing `InstallPlan` with `PlanAction[]`. This feature exposes that plan as structured JSON.

### Design

After the Planning phase completes, serialize the `InstallPlan` to JSON and exit — skipping Elevating and Applying phases entirely. The plan is written to stdout (for piping) or to a file (for archival).

### Key Types

```csharp
// src/FalkForge.Engine/Planning/PlanExporter.cs
public static class PlanExporter
{
    public static string ToJson(InstallPlan plan);
    public static Result<Unit> WriteToFile(InstallPlan plan, string filePath);
}

// src/FalkForge.Engine/Planning/PlanJsonContext.cs
// NativeAOT source-generated JSON serialization context
[JsonSerializable(typeof(PlanOutput))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class PlanJsonContext : JsonSerializerContext;

// src/FalkForge.Engine/Planning/PlanOutput.cs
internal sealed record PlanOutput(
    string Action,
    IReadOnlyList<PlanPackageOutput> Packages,
    IReadOnlyList<PlanFeatureOutput> Features,
    string EstimatedDiskUsage,
    bool RequiresElevation,
    bool RequiresReboot);

// src/FalkForge.Engine/Planning/PlanPackageOutput.cs
internal sealed record PlanPackageOutput(
    string Id,
    string Type,
    string Action,
    string Version,
    IReadOnlyDictionary<string, string> Properties);

// src/FalkForge.Engine/Planning/PlanFeatureOutput.cs
internal sealed record PlanFeatureOutput(
    string Id,
    string Action);
```

### Output Format

```json
{
  "action": "install",
  "packages": [
    {
      "id": "MyApp.msi",
      "type": "MsiPackage",
      "action": "install",
      "version": "1.0.0",
      "properties": {
        "INSTALLFOLDER": "C:\\Program Files\\MyApp"
      }
    }
  ],
  "features": [
    { "id": "MainFeature", "action": "install" },
    { "id": "OptionalDocs", "action": "absent" }
  ],
  "estimatedDiskUsage": "45 MB",
  "requiresElevation": true,
  "requiresReboot": false
}
```

### Integration

`EngineHost` gains a `--plan-only` argument check:

```csharp
// In EngineHost.RunAsync()
if (args.Contains("--plan-only"))
{
    // Run Detecting + Planning only
    var plan = await RunDetectAndPlan();
    var json = PlanExporter.ToJson(plan);
    Console.Write(json);
    return 0;
}
```

The engine state machine transitions `Initializing -> Detecting -> Planning -> Shutdown` without entering `Elevating` or `Applying`.

### CLI

```bash
forge plan installer.csx                  # New command, outputs JSON to stdout
forge plan installer.csx -o plan.json     # Write to file
forge plan installer.csx | jq '.packages' # Pipe to jq for filtering
```

The `forge plan` command is a new `PlanCommand` in `FalkForge.Cli` that delegates to the engine executable with `--plan-only`.

### Use Cases

- **Enterprise change management:** Run plan, get JSON, submit for approval, then execute
- **CI/CD pipelines:** Plan as a build step, fail if unexpected packages/features appear
- **Diffing:** Compare plans between versions to see what changed

### Error Codes

| Code | Description |
|------|-------------|
| PLN001 | Detection phase failed during plan-only mode |
| PLN002 | Planning phase failed during plan-only mode |
| PLN003 | Failed to serialize plan to JSON |

---

## 4. Reproducible MSI Builds

**Effort:** Medium
**Goal:** Same source always produces bit-identical MSI output. First reproducible MSI compiler ever.

### Rationale

Reproducible builds are a supply chain security best practice (reproducible-builds.org). They allow independent verification that a binary was built from claimed source. No MSI compiler — including WiX — produces reproducible output today. Non-determinism in MSI compilation comes from timestamps, GUID generation, file ordering, and record ordering.

### Sources of Non-Determinism

| Source | Current Behavior | Fix |
|--------|-----------------|-----|
| `SummaryInfo.CreateTime` | `DateTime.Now` | Use `SOURCE_DATE_EPOCH` env var |
| `SummaryInfo.LastSaveTime` | `DateTime.Now` | Use `SOURCE_DATE_EPOCH` env var |
| File table timestamps | File system modification time | Normalize to `SOURCE_DATE_EPOCH` |
| Component GUIDs | `Guid.NewGuid()` (random) | Content-based UUID v5 from key path + directory |
| Cabinet file ordering | File system enumeration order | Sort by normalized path (ordinal, case-insensitive) |
| Database record ordering | Insertion order | Sort by primary key before emission |

### Key Changes

**ComponentResolver.cs** — deterministic component IDs:

```csharp
// src/FalkForge.Compiler.Msi/ComponentResolver.cs
public static class ComponentResolver
{
    // Existing: random GUID generation
    // New: content-based deterministic GUID

    private static readonly Guid Namespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8"); // RFC 4122 DNS namespace

    public static Guid DeterministicComponentId(string keyPath, string directory)
    {
        // UUID v5: SHA-1 hash of namespace + name
        var input = $"{directory}\\{keyPath}";
        return CreateUuidV5(Namespace, input);
    }
}
```

User-specified GUIDs always take precedence over generated ones.

**TableEmitter.cs** — deterministic record ordering:

```csharp
// Before emitting each table, sort rows by primary key columns
// MSI spec does not guarantee record order, so sorting is safe
private void EmitTableRows(MsiDatabase db, string tableName,
    IEnumerable<MsiRecord> rows, int[] primaryKeyColumns)
{
    var sorted = rows.OrderBy(r => r.GetString(primaryKeyColumns[0]));
    foreach (var pk in primaryKeyColumns.Skip(1))
        sorted = sorted.ThenBy(r => r.GetString(pk));

    foreach (var row in sorted)
        db.InsertRecord(tableName, row);
}
```

**CabinetBuilder.cs** — deterministic file ordering:

```csharp
// Sort files by normalized relative path before adding to FCI context
var sortedFiles = files
    .OrderBy(f => f.NormalizedPath, StringComparer.OrdinalIgnoreCase)
    .ToList();
```

**SummaryInfoWriter.cs** — `SOURCE_DATE_EPOCH` support:

```csharp
// src/FalkForge.Compiler.Msi/SummaryInfoWriter.cs
private static DateTime GetBuildTimestamp(ReproducibleBuildOptions? options)
{
    if (options?.SourceDateEpoch is { } epoch)
        return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;

    if (Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH") is { } envValue
        && long.TryParse(envValue, out var envEpoch))
        return DateTimeOffset.FromUnixTimeSeconds(envEpoch).UtcDateTime;

    return DateTime.UtcNow; // Non-reproducible fallback
}
```

**ResolvedFile.cs** — normalized path for sorting:

```csharp
// src/FalkForge.Compiler.Msi/ResolvedFile.cs
public string NormalizedPath => Path
    .Replace('/', '\\')
    .TrimStart('\\')
    .ToUpperInvariant();  // Case-normalize for sorting only
```

### Fluent API

```csharp
// src/FalkForge.Core/Builders/ReproducibleBuildOptions.cs
public sealed record ReproducibleBuildOptions
{
    public bool Reproducible { get; init; }
    public long? SourceDateEpoch { get; init; }
}

// Usage
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .Reproducible()  // Enable all deterministic behaviors
    .Feature("Main", f => f.File("MyApp.exe")));
```

### CLI

```bash
forge build installer.csx --reproducible                          # Uses SOURCE_DATE_EPOCH or git commit time
SOURCE_DATE_EPOCH=1708600000 forge build installer.csx --reproducible  # Explicit epoch
```

When `--reproducible` is set and `SOURCE_DATE_EPOCH` is not in the environment, the CLI sets it to the Git commit timestamp of HEAD (`git log -1 --format=%ct`).

### Verification

```bash
# Build twice from identical source
forge build installer.csx --reproducible -o build1.msi
forge build installer.csx --reproducible -o build2.msi

# Verify identical output
sha256sum build1.msi build2.msi
# Expected: identical hashes
```

### Build Metadata

When `SOURCE_DATE_EPOCH` is set, embed it in `SummaryInfo.Comments`:
```
Reproducible build. SOURCE_DATE_EPOCH=1708600000
```

This enables downstream verification that the build was intended to be reproducible.

### Error Codes

| Code | Description |
|------|-------------|
| RPR001 | `SOURCE_DATE_EPOCH` value is not a valid Unix timestamp |

---

## 5. Policy-as-Code Validation

**Effort:** Medium
**Goal:** Composable build-time validation rules for enterprise governance.

### Rationale

Enterprises need to enforce standards across installer projects: signing requirements, scope restrictions, version format compliance, cryptographic minimums. Currently this requires manual review. Policy-as-code allows automated enforcement at build time.

### Design

A new `IInstallerPolicy` interface provides composable validation. Policies run after model validation (which checks structural correctness) but before compilation (which generates output). This ordering ensures policies validate a structurally valid model.

### Key Types

```csharp
// src/FalkForge.Core/Policies/IInstallerPolicy.cs
public interface IInstallerPolicy
{
    string Name { get; }
    ValidationResult Evaluate(PackageModel package);
}

// src/FalkForge.Core/Policies/PolicyRunner.cs
public static class PolicyRunner
{
    public static ValidationResult Evaluate(
        PackageModel package,
        IEnumerable<IInstallerPolicy> policies)
    {
        var errors = new List<ValidationError>();
        foreach (var policy in policies)
        {
            var result = policy.Evaluate(package);
            if (!result.IsValid)
                errors.AddRange(result.Errors);
        }
        return errors.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid(errors);
    }
}
```

### Built-In Policies

Shipped with `FalkForge.Core`:

```csharp
// src/FalkForge.Core/Policies/SigningRequiredPolicy.cs
public sealed class SigningRequiredPolicy : IInstallerPolicy
{
    public string Name => "SigningRequired";
    public ValidationResult Evaluate(PackageModel package)
    {
        if (package.SigningOptions is null)
            return ValidationResult.Invalid(
                new ValidationError("POL001", "Policy 'SigningRequired': SigningOptions must be configured."));
        return ValidationResult.Valid;
    }
}

// src/FalkForge.Core/Policies/NoPerUserInstallPolicy.cs
public sealed class NoPerUserInstallPolicy : IInstallerPolicy
{
    public string Name => "NoPerUserInstall";
    public ValidationResult Evaluate(PackageModel package)
    {
        if (package.InstallScope == InstallScope.User)
            return ValidationResult.Invalid(
                new ValidationError("POL002", "Policy 'NoPerUserInstall': Per-user installation is not allowed."));
        return ValidationResult.Valid;
    }
}

// src/FalkForge.Core/Policies/UpgradeCodeRequiredPolicy.cs
public sealed class UpgradeCodeRequiredPolicy : IInstallerPolicy
{
    public string Name => "UpgradeCodeRequired";
    // Error if UpgradeCode is empty/default GUID
}

// src/FalkForge.Core/Policies/StrongProductVersionPolicy.cs
public sealed class StrongProductVersionPolicy : IInstallerPolicy
{
    public string Name => "StrongProductVersion";
    // Error if version doesn't match X.Y.Z semver pattern
}

// src/FalkForge.Core/Policies/NoWeakCryptoPolicy.cs
public sealed class NoWeakCryptoPolicy : IInstallerPolicy
{
    public string Name => "NoWeakCrypto";
    // Error if DigestAlgorithm is not sha256/sha384/sha512
}
```

### New ErrorKind

```csharp
// Add to src/FalkForge.Core/ErrorKind.cs
PolicyViolation  // value 30 (next available)
```

### Fluent API

```csharp
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .Policy<SigningRequiredPolicy>()
    .Policy<NoPerUserInstallPolicy>()
    .Policy(new CustomPolicy("MyRule", model =>
    {
        // Custom inline validation
        if (string.IsNullOrEmpty(model.Description))
            return ValidationResult.Invalid(
                new ValidationError("POL100", "Product description is required by org policy."));
        return ValidationResult.Valid;
    }))
    .Feature("Main", f => f.File("MyApp.exe")));
```

### Enterprise Patterns

**Company NuGet package with policies:**

```csharp
// In Contoso.InstallerPolicies NuGet package
namespace Contoso.InstallerPolicies;

public sealed class ContosoSigningPolicy : IInstallerPolicy { ... }
public sealed class ContosoVersionPolicy : IInstallerPolicy { ... }
public sealed class ContosoScopePolicy : IInstallerPolicy { ... }
```

**Reference in installer project:**

```xml
<PackageReference Include="Contoso.InstallerPolicies" Version="1.0.0" />
```

```csharp
Installer.Build(p => p
    .Policy<ContosoSigningPolicy>()
    .Policy<ContosoVersionPolicy>()
    .Policy<ContosoScopePolicy>()
    // ... rest of installer definition
```

### CLI

```bash
forge validate installer.csx                              # Validate model + default policies
forge validate installer.csx --policy corp-policies.dll   # Load policies from assembly
forge build installer.csx --policy corp-policies.dll      # Validate before build
```

The `--policy` flag loads the assembly, discovers all types implementing `IInstallerPolicy`, instantiates them (parameterless constructor), and adds them to the policy runner.

### Pipeline Integration

Policy validation is inserted into the compilation pipeline:

```
PackageBuilder.Build()
  → Model Validation (structural correctness)
  → Policy Validation (governance rules)     ← NEW
  → Compilation (MSI/Bundle generation)
```

If any policy returns errors, compilation is aborted and errors are reported.

### Error Codes

| Code | Description |
|------|-------------|
| POL001 | SigningRequired: SigningOptions must be configured |
| POL002 | NoPerUserInstall: Per-user installation is not allowed |
| POL003 | UpgradeCodeRequired: UpgradeCode must be set |
| POL004 | StrongProductVersion: Version must match X.Y.Z format |
| POL005 | NoWeakCrypto: DigestAlgorithm must be sha256 or stronger |
| POL1xx | Reserved for user-defined policy codes |

---

## 6. Accessible Custom UI — WCAG 2.2 AA

**Effort:** Medium
**Goal:** Make FalkForge custom UI installers compliant with the European Accessibility Act (EAA) and WCAG 2.2 Level AA.

### Rationale

The European Accessibility Act (EAA, Directive 2019/882) takes effect June 28, 2025, requiring software products sold in the EU to be accessible. WCAG 2.2 Level AA is the de facto compliance standard. No installer framework addresses accessibility today — installer UIs are notoriously inaccessible.

### Keyboard Navigation

All interactive elements must be operable via keyboard alone.

**Requirements:**
- Tab/Shift+Tab cycles through all interactive elements in logical order
- Enter/Space activates the focused button or control
- Arrow keys navigate feature trees and list controls
- Escape cancels the current operation or closes modal dialogs
- Focus is trapped within modal dialogs (Tab does not escape)
- Visible focus indicators: 2px solid outline with high contrast

**Implementation:**

`InstallerPage` base class gains a `KeyDown` handler routing common keyboard patterns:

```csharp
// src/FalkForge.Ui/InstallerPage.cs (additions)
protected virtual void OnPageKeyDown(KeyEventArgs e)
{
    switch (e.Key)
    {
        case Key.Escape:
            OnEscapePressed();
            e.Handled = true;
            break;
        case Key.Enter when Keyboard.FocusedElement is Button btn:
            btn.Command?.Execute(btn.CommandParameter);
            e.Handled = true;
            break;
    }
}
```

All XAML views must set `TabIndex` and `IsTabStop` explicitly. Focus order follows visual layout: top-to-bottom, left-to-right.

### Screen Reader Support

WPF's UI Automation framework is the foundation for screen reader compatibility (NVDA, Windows Narrator, JAWS).

**Requirements:**
- Every interactive control has `AutomationProperties.Name` set
- Progress and status text uses `AutomationProperties.LiveSetting="Polite"`
- Complex controls (feature tree checkboxes, workload cards) provide `AutomationProperties.HelpText`
- Custom controls implement `AutomationPeer` overrides
- Status changes are announced programmatically

**Implementation:**

```csharp
// src/FalkForge.Ui/Accessibility/AccessibilityHelper.cs
public static class AccessibilityHelper
{
    /// <summary>
    /// Announces a status message to screen readers via UI Automation.
    /// </summary>
    public static void Announce(DependencyObject element, string message)
    {
        AutomationProperties.SetName(element, message);

        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            var peer = UIElementAutomationPeer.FromElement((UIElement)element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}

// src/FalkForge.Ui/InstallerPage.cs (additions)
protected void AnnounceStatus(string text)
{
    // Routes to AccessibilityHelper with the page's status element
    if (_statusElement is not null)
        AccessibilityHelper.Announce(_statusElement, text);
}
```

XAML additions for all views:

```xml
<!-- Example: ProgressPage.xaml -->
<TextBlock x:Name="StatusText"
           AutomationProperties.Name="Installation status"
           AutomationProperties.LiveSetting="Polite"
           Text="{Binding StatusMessage}" />

<ProgressBar AutomationProperties.Name="Installation progress"
             Value="{Binding ProgressPercentage}"
             Minimum="0" Maximum="100" />

<Button AutomationProperties.Name="Cancel installation"
        Content="Cancel"
        Command="{Binding CancelCommand}" />
```

### High Contrast Theme

Windows High Contrast mode must be detected and honored.

**Implementation:**

```csharp
// Detection in InstallerApp initialization
if (SystemParameters.HighContrast)
{
    // Merge high contrast resource dictionary
    var hcTheme = new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/FalkForge.Ui;component/Themes/HighContrastTheme.xaml")
    };
    Application.Current.Resources.MergedDictionaries.Add(hcTheme);
}

// Listen for runtime changes
SystemParameters.StaticPropertyChanged += (s, e) =>
{
    if (e.PropertyName == nameof(SystemParameters.HighContrast))
        ApplyHighContrastTheme(SystemParameters.HighContrast);
};
```

New `HighContrastTheme.xaml`:

```xml
<!-- src/FalkForge.Ui/Themes/HighContrastTheme.xaml -->
<ResourceDictionary>
    <!-- All colors from SystemColors — no hardcoded values -->
    <SolidColorBrush x:Key="WatermarkBrush" Color="{x:Static SystemColors.WindowColor}" />
    <SolidColorBrush x:Key="BannerBrush" Color="{x:Static SystemColors.HighlightColor}" />
    <SolidColorBrush x:Key="TextBrush" Color="{x:Static SystemColors.WindowTextColor}" />
    <SolidColorBrush x:Key="ButtonBrush" Color="{x:Static SystemColors.ControlColor}" />
    <SolidColorBrush x:Key="ButtonTextBrush" Color="{x:Static SystemColors.ControlTextColor}" />
    <SolidColorBrush x:Key="FocusBorderBrush" Color="{x:Static SystemColors.HighlightColor}" />

    <!-- Focus indicator style -->
    <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Style.Triggers>
            <Trigger Property="IsFocused" Value="True">
                <Setter Property="BorderBrush"
                        Value="{DynamicResource FocusBorderBrush}" />
                <Setter Property="BorderThickness" Value="2" />
            </Trigger>
        </Style.Triggers>
    </Style>
</ResourceDictionary>
```

Fluent API:

```csharp
// src/FalkForge.Ui/InstallerWindowBuilder.cs (addition)
public InstallerWindowBuilder HighContrastSupport(bool enabled = true);
```

Default is `true`. Setting to `false` disables automatic high contrast theme detection (not recommended).

### Color Contrast

WCAG 2.2 AA requires:
- Normal text: 4.5:1 contrast ratio against background
- Large text (18pt+ or 14pt+ bold): 3:1 ratio
- Interactive element boundaries: 3:1 ratio against adjacent colors

**Implementation:**

```csharp
// src/FalkForge.Ui/Accessibility/ContrastChecker.cs
public static class ContrastChecker
{
    /// <summary>
    /// Calculates relative luminance per WCAG 2.2 definition.
    /// </summary>
    public static double RelativeLuminance(Color color)
    {
        double R = Linearize(color.R / 255.0);
        double G = Linearize(color.G / 255.0);
        double B = Linearize(color.B / 255.0);
        return 0.2126 * R + 0.7152 * G + 0.0722 * B;
    }

    /// <summary>
    /// Returns contrast ratio between two colors (range: 1:1 to 21:1).
    /// </summary>
    public static double ContrastRatio(Color foreground, Color background)
    {
        double l1 = RelativeLuminance(foreground);
        double l2 = RelativeLuminance(background);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Returns true if the contrast ratio meets WCAG AA for normal text (4.5:1).
    /// </summary>
    public static bool MeetsAA(Color foreground, Color background)
        => ContrastRatio(foreground, background) >= 4.5;

    /// <summary>
    /// Returns true if the contrast ratio meets WCAG AA for large text (3:1).
    /// </summary>
    public static bool MeetsAALargeText(Color foreground, Color background)
        => ContrastRatio(foreground, background) >= 3.0;

    private static double Linearize(double channel)
        => channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
```

**Audit existing themes:**
- Dark theme (`#1E1E1E` background): text must be at least `#949494` (4.5:1 ratio). Current light text colors need verification.
- Classic theme (white background): dark text colors are likely compliant.
- All accent colors used for interactive elements need boundary contrast verification.

### Focus Management

```csharp
// src/FalkForge.Ui/InstallerPage.cs (additions)

/// <summary>
/// Called when this page becomes active. Sets focus to the first interactive element.
/// </summary>
protected virtual void OnNavigatedToAsync()
{
    // Dispatch to allow visual tree to complete layout
    Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        var firstFocusable = FindFirstFocusableElement();
        firstFocusable?.Focus();
    });
}

private UIElement? FindFirstFocusableElement()
{
    // Walk visual tree in tab order to find first focusable element
    return FocusManager.GetFocusScope(View) is DependencyObject scope
        ? FocusManager.GetFocusedElement(scope) as UIElement
        : null;
}
```

Error messages via `Stay(message)` must receive focus:

```csharp
// In page navigation logic, when Stay() is returned:
if (result.Kind == PageResultKind.Stay && result.Message is not null)
{
    // Set focus to error message element
    _errorTextBlock.Text = result.Message;
    _errorTextBlock.Focus();
    AnnounceStatus(result.Message);  // Also announce to screen readers
}
```

### Motion / Animation

```csharp
// Respect Windows "Reduce Motion" setting
if (!SystemParameters.ClientAreaAnimation)
{
    // Disable progress bar animations
    // Disable page transition animations
    // Use instant state changes instead
}
```

### Testing Strategy

| Test Type | Tool | What It Verifies |
|-----------|------|-----------------|
| Automation peers | xUnit + `AutomationPeer.GetPattern()` | All controls have proper automation properties |
| Color contrast | xUnit + `ContrastChecker` | Theme colors meet WCAG AA ratios |
| Keyboard navigation | Integration tests | Tab/Enter/Arrow/Escape sequences work correctly |
| Screen reader | Manual with NVDA + Narrator | Status announcements, control labels, live regions |
| High contrast | Manual on Windows | Theme switches correctly, all content visible |

### Key Files

| File | Action |
|------|--------|
| `src/FalkForge.Ui/Accessibility/ContrastChecker.cs` | New — WCAG color contrast calculator |
| `src/FalkForge.Ui/Accessibility/AccessibilityHelper.cs` | New — screen reader announcement helper |
| `src/FalkForge.Ui/Themes/HighContrastTheme.xaml` | New — high contrast theme resource dictionary |
| `src/FalkForge.Ui/InstallerPage.cs` | Modified — `AnnounceStatus()`, focus management, keyboard handling |
| `src/FalkForge.Ui/InstallerWindowBuilder.cs` | Modified — `HighContrastSupport()` API |
| `src/FalkForge.Ui/Views/*.xaml` | Modified — `AutomationProperties` on every interactive element |

---

## 7. Sigil.Sign Integration

**Effort:** Medium
**Goal:** Integrate FalkForge with the Sigil.Sign CLI tool for code signing, SBOM signing, and supply chain attestation.

### Context

Sigil.Sign (NuGet: `Sigil.Sign`, AGPL-3.0, GitHub: `Falkesand/Sigil`) is a .NET 10 CLI tool supporting:

- **PE/Authenticode signing** — cross-platform (can sign Windows EXEs on Linux)
- **5 cloud HSM backends** — Azure Key Vault, AWS KMS, GCP KMS, HashiCorp Vault, PKCS#11
- **Post-quantum signatures** — ML-DSA-65 (FIPS 204)
- **SLSA attestation** — provenance generation per SLSA v1.0 spec
- **SBOM detection** — detects and embeds SBOM metadata
- **Transparency logs** — Merkle tree audit trail
- **Keyless/OIDC signing** — GitHub Actions, GitLab CI ephemeral identity keys

### License Consideration

Sigil.Sign is AGPL-3.0. FalkForge invokes it as a subprocess via CLI — process execution is widely considered not to trigger AGPL copyleft obligations. There is no library linking, no shared address space, no dynamic loading. FalkForge remains under its own license. The integration is deliberately loose coupling via `IProcessRunner`.

### Signing Provider Abstraction

```csharp
// src/FalkForge.Core/Signing/ISigningProvider.cs
public interface ISigningProvider
{
    string Name { get; }
    Result<Unit> SignFile(string filePath, SigningOptions options);
    Result<Unit> SignPe(string filePath, SigningOptions options);
}
```

Two implementations:

```csharp
// src/FalkForge.Compiler.Msi/Signing/SignToolProvider.cs
// Existing signtool.exe integration (Windows-only, legacy)
public sealed class SignToolProvider : ISigningProvider
{
    public string Name => "signtool";
    // Invokes signtool.exe sign /fd sha256 /tr ... /td sha256 ...
}

// src/FalkForge.Compiler.Msi/Signing/SigilSignProvider.cs
// New Sigil.Sign integration (cross-platform)
public sealed class SigilSignProvider : ISigningProvider
{
    public string Name => "sigil";

    public Result<Unit> SignPe(string filePath, SigningOptions options)
    {
        // Builds CLI args: sigil sign-pe {filePath} [--vault {provider}] [--vault-key {keyId}] ...
        var args = BuildSignPeArgs(filePath, options);
        return _processRunner.Run("sigil", args);
    }

    public Result<Unit> SignFile(string filePath, SigningOptions options)
    {
        // Builds CLI args: sigil sign {filePath} [--vault {provider}] [--vault-key {keyId}] ...
        var args = BuildSignArgs(filePath, options);
        return _processRunner.Run("sigil", args);
    }
}
```

### SigningOptions Expansion

Extend the existing `SigningOptions` model to support Sigil-specific options:

```csharp
// src/FalkForge.Core/Models/SigningOptions.cs (additions)
public sealed record SigningOptions
{
    // Existing fields
    public string? CertificatePath { get; init; }
    public string? CertificateThumbprint { get; init; }
    public string? TimestampUrl { get; init; }
    public string? DigestAlgorithm { get; init; }

    // New fields for Sigil.Sign
    public string? VaultProvider { get; init; }         // "azure" | "aws" | "gcp" | "hashicorp" | "pkcs11"
    public string? VaultKeyId { get; init; }            // Key identifier in vault
    public string? Algorithm { get; init; }             // "ecdsa-p256" | "rsa-pss-3072" | "ml-dsa-65"
    public bool UseKeyless { get; init; }               // OIDC/GitHub Actions ephemeral keys
    public bool GenerateAttestation { get; init; }      // SLSA provenance attestation
    public bool SignSbom { get; init; }                 // Sign SBOM if generated
    public bool UseTransparencyLog { get; init; }       // Record in transparency log
}
```

### Provider Selection

```csharp
// src/FalkForge.Core/Builders/PackageBuilder.cs (addition)
public PackageBuilder SignWith<TProvider>() where TProvider : ISigningProvider, new();
public PackageBuilder SignWith(ISigningProvider provider);
```

### Build Pipeline Integration

After MSI/Bundle compilation, the signing pipeline executes in order:

```
Compilation Complete
  │
  ├─ [1] SBOM Generation (if --sbom)
  │     → {output}.cdx.json
  │
  ├─ [2] Code Signing (if signing configured)
  │     ├─ MSI: sigil sign {output.msi}
  │     └─ Bundle: sigil sign-pe {output.exe}
  │
  ├─ [3] SBOM Signing (if SignSbom)
  │     → sigil sign {output.cdx.json}
  │     → {output}.cdx.json.sig
  │
  ├─ [4] Attestation (if GenerateAttestation)
  │     → sigil attest {output} --predicate-type https://slsa.dev/provenance/v1
  │     → {output}.att.json
  │
  └─ [5] Transparency Log (if UseTransparencyLog)
        → sigil log append {output.sig.json}
```

Output artifacts:

| File | Description |
|------|-------------|
| `output.msi` or `output.exe` | Signed installer |
| `output.cdx.json` | CycloneDX SBOM (from section 1) |
| `output.cdx.json.sig` | Signed SBOM (detached signature) |
| `output.att.json` | SLSA provenance attestation |

### Bundle Detach/Reattach with Sigil

For HSM signing, the PE stub must be signed separately from the payload data. The existing `BundleDetacher` workflow integrates:

```
1. forge bundle detach output.exe → stub.exe + data.bin
2. sigil sign-pe stub.exe --vault azure --vault-key my-signing-key
3. forge bundle reattach stub.exe data.bin output-signed.exe
```

The CLI provides a shortcut that automates all three steps:

```bash
forge build installer.csx --sign sigil --vault azure --vault-key my-key
```

Internally:
1. Compile bundle → `output.exe`
2. `BundleDetacher.Detach("output.exe", "stub.exe", "data.bin")`
3. `SigilSignProvider.SignPe("stub.exe", options)`
4. `BundleDetacher.Reattach("stub.exe", "data.bin", "output-signed.exe")`

### CLI Usage

```bash
# Basic signing with Sigil
forge build installer.csx --sign sigil

# Cloud HSM signing
forge build installer.csx --sign sigil --vault azure --vault-key my-key
forge build installer.csx --sign sigil --vault aws --vault-key alias/my-key
forge build installer.csx --sign sigil --vault gcp --vault-key projects/p/locations/l/keyRings/r/cryptoKeys/k

# Keyless signing (CI/CD with OIDC identity)
forge build installer.csx --sign sigil --keyless

# Post-quantum algorithm
forge build installer.csx --sign sigil --algorithm ml-dsa-65

# Full supply chain security package
forge build installer.csx --sbom --sign sigil --attest --transparency-log

# Legacy signtool (Windows only)
forge build installer.csx --sign signtool --certificate-thumbprint ABC123
```

### Fluent API

```csharp
// Cloud HSM signing with attestation
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .SignWith<SigilSignProvider>()
    .Signing(s => s
        .VaultProvider("azure")
        .VaultKeyId("my-signing-key")
        .GenerateAttestation()
        .SignSbom()
        .UseTransparencyLog())
    .Sbom()
    .Feature("Main", f => f.File("MyApp.exe")));

// Keyless signing in GitHub Actions
Installer.Build(p => p
    .Product("MyApp", "1.0.0", "Contoso")
    .SignWith<SigilSignProvider>()
    .Signing(s => s
        .Keyless()
        .GenerateAttestation())
    .Feature("Main", f => f.File("MyApp.exe")));
```

### Error Codes

| Code | Description |
|------|-------------|
| SGN001 | Sigil.Sign CLI tool not found on PATH |
| SGN002 | Sigil.Sign returned non-zero exit code |
| SGN003 | Vault provider not recognized |
| SGN004 | Vault key ID required when vault provider is specified |
| SGN005 | Keyless signing requires OIDC environment (CI/CD) |
| SGN006 | SBOM signing requested but no SBOM was generated |
| SGN007 | Attestation generation failed |
| SGN008 | Transparency log append failed |

---

## Summary: The Complete USP Story

After implementing all 7 features, FalkForge delivers an end-to-end supply chain security story that no competitor offers:

```
Source Code
  │
  ├─ Policy-as-Code ──── Enterprise governance at build time
  │
  ├─ Reproducible Build ── Bit-identical MSI from same source
  │
  ├─ SBOM Generation ──── CycloneDX 1.6 bill of materials
  │
  ├─ Sigil.Sign ──────── HSM signing + SLSA attestation + transparency log
  │
  ├─ WinGet Manifest ──── One-command distribution
  │
  ├─ Dry-Run Planning ─── JSON preview for change management
  │
  └─ Accessible UI ────── WCAG 2.2 AA / EU Accessibility Act
```

**No other framework — WiX, InstallShield, Advanced Installer, MSIX, or Inno Setup — offers any of these capabilities.** FalkForge becomes the only installer framework where:

- Every build produces a signed SBOM with provenance attestation
- Builds are reproducible and independently verifiable
- Enterprise policies are enforced at compile time, not by code review
- Installation plans can be reviewed before execution
- The installer UI is accessible to users with disabilities
- Distribution to WinGet is a single CLI flag
- Signing works with any HSM, on any platform, including post-quantum algorithms
