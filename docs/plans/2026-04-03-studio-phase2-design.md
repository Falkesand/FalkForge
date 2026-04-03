# Studio Phase 2: Live Validation, Build Output, Project Templates

Three workflow features for FalkForge.Studio that improve editing feedback, build visibility, and new-project onboarding.

## 1. Live Validation Panel

### Architecture

The validation panel sits in the existing bottom tabbed area alongside Output. It shows real-time errors and warnings as the user edits any field across the 23 editors.

**Data flow:**
1. Each `*Section` model property setter calls `OnPropertyChanged()` (already exists)
2. `StudioViewModel` subscribes to model changes and debounces validation (300ms)
3. Validation runs the existing FalkForge.Core validators (PKG001-011, FEA001-005, etc.) against the current `StudioProject` state
4. Results populate an `ObservableCollection<ValidationItem>` bound to a DataGrid in the bottom panel
5. Clicking a row calls `NavigateTo(editorKey)` to jump to the relevant editor

### ValidationItem

```csharp
internal sealed record ValidationItem(
    ValidationSeverity Severity,  // Error, Warning, Info
    string Code,                  // e.g. "PKG003"
    string Message,
    string EditorKey,             // maps to tree node for navigation
    string? PropertyName);        // optional, for field-level focus
```

### ValidationService

New class in `Shell/`. Accepts `StudioProject`, runs all applicable validators based on project type (MSI -> ModelValidator, Bundle -> BundleValidator, MSIX -> MsixValidator). Returns `IReadOnlyList<ValidationItem>`. Maps error codes to editor keys via a static dictionary (e.g. PKG* -> "Product", FEA* -> "Features", BDL* -> "BundleSettings"). Pure logic, no UI dependencies.

### Debounce

`StudioViewModel` holds a `CancellationTokenSource` that resets on each model change. After 300ms without changes, fires validation on `Task.Run`, then dispatches results to UI thread via `Dispatcher.InvokeAsync`.

### UI

- "Validation" tab in bottom panel with DataGrid: Severity icon (Unicode glyphs), Code, Message, Editor
- Severity icons: red circle for error, yellow triangle for warning, blue circle for info
- Double-click or Enter navigates to the editor
- Tab header shows badge with error count when > 0: "Validation (3)"

### Trigger Points

- Any property change on any `*Section` model
- After file add/remove operations
- After feature tree modifications
- NOT during build (build has its own output)

## 2. Build Output & Progress

### Architecture

Redesign the existing Output tab with three stacked elements:

1. **Progress bar** (top, 4px) — indeterminate during build, proportional when compiler reports steps. Hidden when idle.
2. **Build log** (middle, scrolling) — timestamped lines, auto-scroll, color-coded: white info, yellow warnings, red errors.
3. **Summary strip** (bottom, 24px) — appears after build. "Build succeeded in 2.3s" (green) or "Build failed - 3 errors, 1 warning" (red). Clicking error count switches to Validation tab.

### BuildProgressService

New class in `Shell/`. Wraps `StudioBuildService.Compile()` with progress reporting.

```csharp
internal sealed record BuildProgressStep(string Phase, int Percent);

internal sealed record BuildResult(
    bool Success,
    string? OutputPath,
    TimeSpan Duration,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
```

Phases: "Validating", "Compiling", "Signing", "Writing".

### StudioViewModel Integration

- `IsBuildInProgress` property shows progress bar, disables editors
- On failure: auto-switch to Validation tab, populate with build errors
- On success: summary strip with "Open folder" hyperlink to output directory

## 3. Project Templates

### Architecture

When File -> New is selected, show a modal template selection dialog with a grid of template cards and name/manufacturer text fields.

### ProjectTemplateService

New class in `Project/`. Returns built-in templates as static factory methods.

```csharp
internal sealed record ProjectTemplate(
    string Name,
    string Description,
    string IconGlyph,        // Unicode character
    ProjectType ProjectType,
    Func<string, string, StudioProject> CreateProject);
```

### Built-in Templates (6)

| Template | Pre-populates |
|----------|--------------|
| Simple Application | Single feature, INSTALLDIR, desktop shortcut, major upgrade |
| Windows Service | Service component, LocalSystem, auto-start, firewall rule |
| Client-Server | Two features (Client + Server), service + desktop shortcut |
| Enterprise Suite | Feature tree (3 levels), install scope, custom install dir |
| EXE Bundle | Bundle project type, 2 MSI packages, rollback boundary |
| MSIX Package | MSIX project type, app identity, capabilities |

### Integration

`StudioViewModel.NewCommand` opens the dialog. On create, replaces `CurrentProject`, rebuilds the tree, navigates to Product editor.

## Files

| Action | File | Purpose |
|--------|------|---------|
| Create | `src/FalkForge.Studio/Shell/ValidationService.cs` | Validation logic |
| Create | `src/FalkForge.Studio/Shell/ValidationItem.cs` | Validation result record |
| Create | `src/FalkForge.Studio/Shell/ValidationSeverity.cs` | Severity enum |
| Create | `src/FalkForge.Studio/Shell/BuildProgressService.cs` | Build progress wrapper |
| Create | `src/FalkForge.Studio/Shell/BuildProgressStep.cs` | Progress step record |
| Create | `src/FalkForge.Studio/Shell/BuildResult.cs` | Build result record |
| Create | `src/FalkForge.Studio/Project/ProjectTemplateService.cs` | Template factory |
| Create | `src/FalkForge.Studio/Project/ProjectTemplate.cs` | Template record |
| Create | `src/FalkForge.Studio/Shell/NewProjectDialog.xaml` | Template selection UI |
| Create | `src/FalkForge.Studio/Shell/NewProjectDialog.xaml.cs` | Dialog code-behind |
| Edit | `src/FalkForge.Studio/Shell/StudioWindow.xaml` | Add Validation tab, redesign Output tab |
| Edit | `src/FalkForge.Studio/Shell/StudioViewModel.cs` | Wire validation, build progress, new project |
| Create | `tests/FalkForge.Studio.Tests/ValidationServiceTests.cs` | Validation mapping tests |
| Create | `tests/FalkForge.Studio.Tests/BuildProgressServiceTests.cs` | Build progress tests |
| Create | `tests/FalkForge.Studio.Tests/ProjectTemplateServiceTests.cs` | Template creation tests |

**10 new src files, 2 edited src files, 3 new test files.**

## Implementation Order

1. ValidationItem + ValidationSeverity + ValidationService + tests
2. StudioWindow.xaml Validation tab + StudioViewModel wiring
3. BuildProgressStep + BuildResult + BuildProgressService + tests
4. StudioWindow.xaml Output tab redesign + StudioViewModel wiring
5. ProjectTemplate + ProjectTemplateService + tests
6. NewProjectDialog.xaml + StudioViewModel NewCommand wiring
