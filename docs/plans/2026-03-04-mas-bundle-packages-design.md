# MAS Bundle with MSI Packages — Design

## Context

The MAS (MultiAccess Suite) demo is a custom UI installer with 10 wizard pages. It currently runs in design-time mode with `NullInstallerEngine` — no actual MSI packages exist and no installation occurs. The installer references 5 products by name only (UI strings).

This design adds real MSI package projects, a bundle project that chains them, and completes the engine wiring so MAS can actually detect and install packages.

## Architecture

```
MASInstaller.exe (bundle — self-extracting bootstrapper)
  ├── manifest.json (package metadata, chain order)
  ├── MultiAccess.msi
  ├── MultiServer.msi
  ├── MultiServerEx.msi
  ├── Konfigurera.msi
  ├── Concatenate.msi
  └── MAS.exe (custom UI)

Runtime:
  MASInstaller.exe → extracts payloads
    → launches FalkForge.Engine.exe (--manifest, --pipe)
    → launches MAS.exe (--manifest, --pipe)
    → engine detects → UI wizard → engine installs silently
```

## Components

### 1. Dummy Stub EXE

**File:** `demo/MAS/packages/stub/dummy.exe`

A minimal Windows PE executable. Each MSI packages a copy renamed to match the product (e.g., `MultiAccess.exe`). This is a pre-built file, not compiled from source.

### 2. Five MSI Package Projects

**Location:** `demo/MAS/packages/<Name>/<Name>.csproj`

Each project:
- Is a console app using `Installer.Build()` + `MsiCompiler`
- Packages `dummy.exe` (renamed) + `version.txt` into a single MSI feature
- Installs to `Program Files\ASSA ABLOY\<ProductName>\`
- Has a stable hardcoded `UpgradeCode` (unique GUID per product)
- Version: 8.9.0
- Silent install only (no MSI UI)

Products:

| Product | Install Path | UpgradeCode |
|---------|-------------|-------------|
| MultiAccess | `ASSA ABLOY\MultiAccess\` | (generate stable GUID) |
| MultiServer | `ASSA ABLOY\MultiServer\` | (generate stable GUID) |
| MultiServerEx | `ASSA ABLOY\MultiServerEx\` | (generate stable GUID) |
| Konfigurera | `ASSA ABLOY\Konfigurera\` | (generate stable GUID) |
| Concatenate | `ASSA ABLOY\Concatenate\` | (generate stable GUID) |

### 3. Bundle Project

**File:** `demo/MAS/bundle/MASBundle.csproj` + `Program.cs`

Uses `BundleBuilder` to:
- Chain the 5 MSIs in order (all `Vital(true)`)
- Reference MAS.exe as custom UI via `UseCustomUI()`
- Compile to `MASInstaller.exe`

Has `<ProjectReference>` to all 5 MSI projects so they build first.

### 4. Engine Wiring — ResolveEngine()

**File:** `src/FalkForge.Ui/InstallerApp.cs`

Change `ResolveEngine()` from returning `null` to:
- When `--manifest` and `--pipe` args are present:
  - Load manifest JSON from file path
  - Create `EngineClient` with pipe connection options
  - Return the connected client
- Otherwise: return `null` (design-time mode preserved)

### 5. Engine Wiring — Manifest Loading

**File:** `src/FalkForge.Engine/Program.cs`

Change the stub to:
- Read manifest JSON from `--manifest` file path
- Deserialize to `InstallerManifest`
- Pass to `EngineHost` constructor
- Start the engine state machine

### 6. Install Progress Page

**Files:** `demo/MAS/Pages/InstallProgressPage.cs` + `demo/MAS/Views/InstallProgressView.xaml`

- Subscribes to `IInstallerEngine.Progress` and `StatusMessage` observables
- Shows overall progress bar + per-package status
- Shows current operation text (e.g., "Installing MultiServer...")
- No Back/Cancel during installation (buttons disabled)
- On completion, auto-navigates to CompletionPage

### 7. Completion Page

**Files:** `demo/MAS/Pages/CompletionPage.cs` + `demo/MAS/Views/CompletionView.xaml`

- Shows success or failure message
- On failure: shows error details
- "Finish" button closes the installer
- No Back button

### 8. Localization

Add to `strings.en-US.json` and `strings.sv-SE.json`:
- `InstallProgress.Title` — "Installing" / "Installerar"
- `InstallProgress.StatusLabel` — "Status:" / "Status:"
- `InstallProgress.InstallingFormat` — "Installing {0}..." / "Installerar {0}..."
- `Completion.Title` — "Installation Complete" / "Installationen klar"
- `Completion.SuccessBody` — success message
- `Completion.FailureBody` — failure message
- `Completion.FinishButton` — "Finish" / "Slutför"

## Flow

1. User launches `MASInstaller.exe`
2. Bundle extracts payloads, launches engine + MAS.exe
3. MAS connects to engine via pipe (`ResolveEngine()`)
4. Engine detects installed state of all 5 packages
5. User walks through wizard (Welcome → License → Type → DB → Confirm)
6. User clicks "Install" on ConfirmParametersPage
7. ConfirmParametersPage navigates to InstallProgressPage
8. InstallProgressPage calls `engine.PlanAsync(Install)` then `engine.ApplyAsync()`
9. Engine silently installs each MSI in chain order
10. Progress page shows real-time progress from engine
11. On completion, navigates to CompletionPage
12. User clicks "Finish"

## Verification

1. `dotnet build D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx` — 0 errors
2. Each MSI project produces a valid `.msi` file
3. Bundle project produces `MASInstaller.exe`
4. MAS still works in design-time mode (no args → NullInstallerEngine)
5. All existing tests pass
