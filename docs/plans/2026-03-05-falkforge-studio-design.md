# FalkForge Studio — Architecture Design

## Vision

A WPF visual installer builder that provides a GUI over the existing FalkForge builder APIs. Users create MSI packages and bundles by editing forms, dragging files, and clicking Build — no C# code required. The Studio generates the same artifacts as the CLI: MSI files, EXE bundles, and optionally a C# project file for advanced users.

## Key Principle

**Studio is a thin visual layer over existing APIs.** It does not introduce a new compilation pipeline, model layer, or build system. Every operation maps directly to `PackageBuilder` / `BundleBuilder` method calls. The JSON project format is a superset of the existing `InstallerConfig` schema used by `JsonConfigLoader`.

---

## 1. Application Architecture

### Shell

WPF application (`net10.0-windows`) using the existing MVVM patterns from `FalkForge.Ui.Abstractions`:

```
FalkForge.Studio (WPF exe)
├── Shell/
│   ├── StudioWindow.xaml          — Main window with tree nav + editor area + output
│   └── StudioViewModel.cs         — Orchestrates navigation, dirty tracking, build
├── Editors/                        — One UserControl + ViewModel per tree node type
├── Services/                       — Project loading, building, validation
└── Converters/                     — WPF value converters
```

**Main window layout:**

```
┌─────────────────────────────────────────────────────────┐
│  Menu: File | Edit | Build | Tools | Help               │
├───────────┬─────────────────────────────────────────────┤
│ TreeView  │  Editor Area (context-sensitive)            │
│           │                                             │
│ ▼ Product │  [Product Info Editor]                      │
│   Info    │  Name: [___________]                        │
│   Files   │  Manufacturer: [___________]                │
│   Features│  Version: [___________]                     │
│   Registry│  ...                                        │
│   Services│                                             │
│   UI      │                                             │
│ ▼ Bundle  │                                             │
│   Packages│                                             │
│   Chain   │                                             │
│   UI      │                                             │
├───────────┴─────────────────────────────────────────────┤
│ Output: Build succeeded. 1 MSI, 0 warnings.            │
└─────────────────────────────────────────────────────────┘
```

### Navigation

The tree navigator reflects the installer structure:

- **Product** — name, manufacturer, version, upgrade code, architecture, scope
- **Files & Directories** — file sources, install directories, harvesting
- **Features** — feature tree with nested features
- **Registry** — registry keys and values
- **Services** — Windows services, service controls
- **Shortcuts** — desktop/start menu shortcuts
- **UI & Dialogs** — dialog set selection, license file
- **Environment** — environment variables
- **Custom Actions** — custom action definitions
- **Extensions** — SQL, HTTP, Firewall, Scheduled Tasks, Perf Counters, ODBC, XML Config
- **Build Settings** — output path, compression, signing, cabinet threads
- **Bundle** (separate tree root for bundle projects)
  - Packages — MSI/EXE/MSU/.NET Runtime packages
  - Chain — package ordering, rollback boundaries
  - Detection — search conditions per package
  - UI — built-in/custom/silent
  - Variables & Features

---

## 2. Project System

### Project File Format (`.ffstudio`)

A JSON file that extends the existing `InstallerConfig` schema. It adds Studio-specific metadata (editor state, UI hints) while the core data maps directly to builder API calls.

```json
{
  "$schema": "https://falkforge.dev/studio/v1.json",
  "projectType": "msi",
  "product": {
    "name": "My Application",
    "manufacturer": "Acme Corp",
    "version": "1.0.0",
    "upgradeCode": "12345678-...",
    "architecture": "x64",
    "scope": "perMachine"
  },
  "installDirectory": "ProgramFiles/Acme/MyApp",
  "features": [
    {
      "id": "Main",
      "title": "Core Files",
      "files": [
        { "source": "bin/Release/**", "exclude": ["*.pdb"] }
      ]
    }
  ],
  "ui": {
    "dialogSet": "FeatureTree",
    "licenseFile": "assets/license.rtf"
  },
  "registry": [],
  "services": [],
  "shortcuts": [],
  "extensions": {
    "sql": null,
    "firewall": null,
    "scheduledTasks": null
  },
  "build": {
    "outputPath": "out/",
    "compression": "High",
    "signing": null,
    "cabinetThreads": 4
  }
}
```

For bundle projects, `projectType: "bundle"` adds a `bundle` section:

```json
{
  "projectType": "bundle",
  "bundle": {
    "name": "My Installer",
    "manufacturer": "Acme Corp",
    "version": "1.0.0",
    "upgradeCode": "...",
    "ui": "builtIn",
    "packages": [
      {
        "type": "MsiPackage",
        "source": "setup.msi",
        "id": "MainMsi",
        "displayName": "My Application",
        "vital": true,
        "detectionMode": "Default",
        "searchConditions": [],
        "isPrerequisite": false
      }
    ],
    "chain": ["MainMsi"],
    "downloadThrottle": 0
  }
}
```

### Load / Save Pipeline

```
Load:  .ffstudio JSON → StudioProjectModel → populate ViewModels
Save:  ViewModels → StudioProjectModel → JSON serialization
Build: StudioProjectModel → PackageBuilder/BundleBuilder calls → Model → Compiler → MSI/EXE
```

The `StudioProjectModel` is the single source of truth. It maps to/from builder APIs:

| Studio Section | Builder API | Model |
|---------------|-------------|-------|
| Product | `PackageBuilder.Name/Manufacturer/Version/...` | `PackageModel` |
| Files | `PackageBuilder.Files(...)` | `FileEntryModel` |
| Features | `PackageBuilder.Feature(...)` | `FeatureModel` |
| Registry | `PackageBuilder.Registry(...)` | `RegistryEntryModel` |
| Services | `PackageBuilder.Service(...)` | `ServiceModel` |
| Shortcuts | `PackageBuilder.Shortcut(...)` | `ShortcutModel` |
| UI | `PackageBuilder.UseDialogSet(...)` | `MsiDialogSet` |
| Build | `MsiCompiler.Compile(model, outputPath)` | `PackageModel` |
| Bundle Packages | `BundlePackageBuilder.*` | `BundlePackageModel` |
| Bundle | `BundleBuilder.*` | `BundleModel` |
| Extensions | `IMsiTableContributor` via plugin system | Extension models |

### Export to C# Project

"Export to Code" generates a C# program using the fluent builder API — the inverse of the decompiler's `BundleCSharpEmitter`. This allows advanced users to customize beyond what the GUI supports.

---

## 3. Editor Components

Each editor is a WPF `UserControl` + `ViewModel`. Editors bind to observable properties on the `StudioProjectModel`.

### 3.1 Product Info Editor

Simple form: text fields for Name, Manufacturer, Version, Description, Contact, URLs. Dropdown for Architecture (x86/x64/arm64), Scope (perUser/perMachine). Auto-generated UpgradeCode with manual override.

### 3.2 Files & Directories Editor

Two-pane layout:
- **Left:** Source file browser with checkboxes (like a file picker)
- **Right:** Target directory tree showing where files install

Supports:
- Drag-drop from Windows Explorer
- Glob patterns (`bin/**/*.dll`, exclude `*.pdb`)
- Directory harvesting (add folder → all contents included)
- Per-file feature assignment

Maps to `PackageBuilder.Files(fs => fs.Add(...))`.

### 3.3 Features Editor

TreeView with drag-drop reordering. Each feature node has:
- ID, Title, Description
- Default install state (local/absent/advertised)
- Assigned files (from Files editor)
- Nested child features

Maps to `PackageBuilder.Feature(...)` with nested `FeatureBuilder` calls.

### 3.4 Registry Editor

Grid/table editor:
- Root (HKLM/HKCU/HKCR/HKU)
- Key path
- Value name, type (String/DWord/ExpandString/MultiString/Binary), data

Maps to `PackageBuilder.Registry(...)`.

### 3.5 Services Editor

Form per service:
- Service name, display name, description
- Start type (auto/demand/disabled)
- Service type (ownProcess/shareProcess)
- Account (LocalSystem/LocalService/NetworkService/custom)
- Dependencies, arguments

Maps to `PackageBuilder.Service(...)` and `PackageBuilder.ServiceControl(...)`.

### 3.6 UI & Dialogs Editor

Dropdown for dialog set: None, Minimal, InstallDir, FeatureTree, Mondo, Advanced.
File picker for license RTF.
For bundles: Built-in / Custom / Silent radio selection.

Maps to `PackageBuilder.UseDialogSet(...)` / `BundleBuilder.UseBuiltInUI(...)`.

### 3.7 Bundle Packages Editor

List of packages with add/remove/reorder. Each package has a detail panel:
- Type (MSI/EXE/MSU/NetRuntime)
- Source path, display name, ID
- Install condition
- Detection mode + search conditions
- Exit code mappings
- Vital, Prerequisite flags
- Authenticode thumbprint
- Slipstream target

Maps to `BundlePackageBuilder.*`.

### 3.8 Extensions Editors

Each extension type (SQL, Firewall, HTTP, Scheduled Tasks, Perf Counters, ODBC, XML Config) gets its own sub-editor. These are loaded dynamically via the `IInstallerPlugin` / `IMsiTableContributor` system.

The 8 existing contributors:
- `DependencyTableContributor`
- `FirewallTableContributor`
- `HttpCustomActionContributor` + `HttpSequenceContributor`
- `SqlDatabaseTableContributor` + `SqlScriptTableContributor` + `SqlStringTableContributor`
- `XmlConfigTableContributor`

Plus the new ones from WiX parity: Scheduled Tasks, Perf Counters, ODBC.

---

## 4. Build Integration

### Build Pipeline

Studio calls the same compiler APIs as the CLI. No separate build system:

```csharp
// MSI build
var builder = new PackageBuilder();
ApplyProjectToBuilder(project, builder);  // maps JSON → builder calls
var model = builder.Build();
var result = MsiCompiler.Compile(model, outputPath);

// Bundle build
var bundleBuilder = new BundleBuilder();
ApplyBundleToBuilder(project, bundleBuilder);
var bundleModel = bundleBuilder.Build();
var result = BundleCompiler.Compile(bundleModel, outputPath);
```

### Validation

Real-time validation as the user edits:
- Required fields (Name, Manufacturer, Version)
- GUID format (UpgradeCode)
- File path existence
- Feature tree completeness (files assigned)
- Extension configuration validity

Validation runs on `PackageBuilder.Build()` — builder already validates and returns `Result<T>`.

### Output Pane

Displays: build progress, warnings, errors, output file path. Same output as CLI `falkforge build`.

---

## 5. Project Structure

```
src/FalkForge.Studio/                        — WPF application (net10.0-windows)
├── FalkForge.Studio.csproj
├── App.xaml / App.xaml.cs
├── Shell/
│   ├── StudioWindow.xaml                    — Main window
│   └── StudioViewModel.cs                   — Navigation, dirty tracking
├── Project/
│   ├── StudioProjectModel.cs                — Central project data model
│   ├── StudioProjectLoader.cs               — JSON load/save
│   └── StudioBuildService.cs                — PackageBuilder/BundleBuilder orchestration
├── Editors/
│   ├── ProductEditor/
│   │   ├── ProductEditorView.xaml
│   │   └── ProductEditorViewModel.cs
│   ├── FilesEditor/
│   │   ├── FilesEditorView.xaml
│   │   └── FilesEditorViewModel.cs
│   ├── FeaturesEditor/
│   │   ├── FeaturesEditorView.xaml
│   │   └── FeaturesEditorViewModel.cs
│   ├── RegistryEditor/
│   │   ├── RegistryEditorView.xaml
│   │   └── RegistryEditorViewModel.cs
│   ├── ServicesEditor/
│   │   ├── ServicesEditorView.xaml
│   │   └── ServicesEditorViewModel.cs
│   ├── ShortcutsEditor/
│   ├── UiEditor/
│   ├── EnvironmentEditor/
│   ├── CustomActionsEditor/
│   ├── BuildSettingsEditor/
│   ├── BundlePackagesEditor/
│   ├── BundleChainEditor/
│   ├── BundleUiEditor/
│   └── Extensions/
│       ├── SqlEditor/
│       ├── FirewallEditor/
│       ├── HttpEditor/
│       ├── ScheduledTasksEditor/
│       ├── PerfCountersEditor/
│       ├── OdbcEditor/
│       └── XmlConfigEditor/
├── Navigation/
│   ├── TreeNodeViewModel.cs
│   └── NavigationService.cs
├── Converters/
│   └── (WPF value converters)
└── Themes/
    └── (Resource dictionaries)
```

References: `FalkForge.Core`, `FalkForge.Compiler`, `FalkForge.Compiler.Bundle`, `FalkForge.Extensibility`, all extension projects.

---

## 6. Time Estimates

### Manual (1 Senior Developer)

| Phase | Scope | Duration |
|-------|-------|----------|
| **MVP** | Shell, project system, product editor, files editor, features editor, build | 8-12 weeks |
| **v1.0** | All editors, validation, bundle support, drag-drop, extensions | 20-30 weeks |
| **v2.0** | Export to code, import WiX, custom UI designer, polish | 40-50 weeks |

### With Claude Code

| Phase | Scope | Duration | Acceleration |
|-------|-------|----------|-------------|
| **MVP** | Shell, project system, product editor, files editor, features editor, build | 3-4 weeks | 3x |
| **v1.0** | All editors, validation, bundle support, drag-drop, extensions | 7-10 weeks | 3x |
| **v2.0** | Export to code, import WiX, custom UI designer, polish | 14-18 weeks | 2.5x |

**Where Claude Code accelerates most (3-5x):**
- XAML generation for editors (repetitive form layouts)
- ViewModel scaffolding with INotifyPropertyChanged
- JSON serialization models (mechanical mapping)
- Test generation for load/save round-trips
- Builder API mapping code (project model → builder calls)
- Extension editor boilerplate (8+ similar editors)

**Where acceleration is lower (1.5-2x):**
- UX polish, drag-drop edge cases, visual design
- TreeView interaction behaviors
- File system integration (Explorer drag-drop)
- Cross-editor state synchronization
- Complex validation rules

---

## 7. MVP Scope (v0.1)

The minimum viable product covers:

1. **Shell** — main window with tree navigation + editor area + output pane
2. **Project system** — new/open/save `.ffstudio` JSON files
3. **Product editor** — name, manufacturer, version, upgrade code, architecture, scope
4. **Files editor** — add files/folders, assign to install directory
5. **Features editor** — create features, assign files
6. **Build** — compile to MSI via `PackageBuilder` → `MsiCompiler`
7. **Validation** — real-time field validation with error display

MVP does NOT include: bundle support, extensions, drag-drop, export to code, import, signing, custom actions, services, registry.

---

## 8. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| WPF TreeView drag-drop is tricky | Medium | Use proven DragDrop library (GongSolutions.WPF.DragDrop) |
| File glob expansion at design time | Low | Lazy evaluation, show patterns not expanded files |
| Builder validation errors unclear to end user | Medium | Map Result<T> errors to user-friendly messages |
| Extension editors vary wildly | Low | Common base editor pattern, per-extension customization |
| Large projects slow to load | Low | Async loading with progress, lazy feature tree |
