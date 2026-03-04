# Demo 12: Custom UI Visual Studio Style

A Visual Studio-inspired installer UI with a dark theme, borderless window, workload selection, and per-component progress tracking. This demo recreates the familiar VS Installer experience using the FalkForge.Ui framework.

## What This Demonstrates

- Borderless window with dark background and custom accent color
- Multi-step wizard: product landing page, workload selection, progress, completion
- Workload/component selection model with `ObservableCollection<Workload>` and component-level granularity
- Computed properties (`TotalSelectedSize`) that react to selection changes
- Detection-aware pages using `DetectedState` and `InstallState` to show different UI for fresh installs vs. upgrades
- Page validation with `PageResult.Stay(errorMessage)` to block navigation when no workloads are selected
- Separate `CanGoNext` and `CanGoBack` control per page (progress page disables both)

## Key API Calls

```csharp
InstallerApp.Run(args, app => app
    .Localization(loc => loc ...)
    .Window(w => w
        .Size(1024, 700)
        .Borderless()                  // Remove window chrome
        .Background("#1E1E1E")         // Dark background
        .Accent("#7B68EE")             // Purple accent
        .Title("FalkForge DevTools Suite Installer"))
    .Pages(p => p
        .Add<ProductPage>()
        .Add<WorkloadsPage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));

// Detection-aware UI
public bool IsInstalled => DetectedState == InstallState.Installed;

// Validation before proceeding
public override PageResult OnNext()
{
    if (!Workloads.Any(w => w.IsSelected))
        return PageResult.Stay(Localize("Workloads.SelectAtLeastOne"));
    return PageResult.Install;
}
```

## How to Build

```
dotnet build demo/12-custom-ui-vstyle/12-custom-ui-vstyle.csproj
```

## Notes

- The borderless window relies on `Borderless()` which removes the standard Windows title bar. Custom drag/close/minimize controls must be provided in the XAML views.
- Workload data is hardcoded in the `WorkloadsPage` view-model for demonstration. In a real installer, this could be loaded from a manifest or the bundle chain.
- `PageResult.Stay(message)` prevents navigation and can display a validation error to the user.
- The `ProgressPage` sets both `CanGoBack` and `CanGoNext` to `false`, locking the user on the page during installation.
