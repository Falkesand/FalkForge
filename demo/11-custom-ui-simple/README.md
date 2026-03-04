# Demo 11: Custom UI Simple

A minimal custom UI installer using the FalkForge.Ui framework. This demo shows the simplest possible page-based installer with localization support, a welcome screen, progress tracking, and a completion page.

## What This Demonstrates

- Building a WPF-based installer UI with `InstallerApp.Run`
- Defining pages by subclassing `InstallerPage<TView>` with MVVM view-model binding
- Configuring window size, title, and accent color via the fluent API
- Loading JSON localization resources from embedded assembly resources
- Automatic culture detection with `DetectCulture()` and user-selectable language via `AllowLanguageSelection()`
- Page navigation using `PageResult.Next`, `PageResult.Install`, and `PageResult.Finish`
- Property change notification with `SetField` for progress bar binding

## Key API Calls

```csharp
// Bootstrap the installer UI
InstallerApp.Run(args, app => app
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<WelcomePage>("lang.strings.en-US.json")
        .AddJsonResource<WelcomePage>("lang.strings.sv-SE.json")
        .DetectCulture()
        .AllowLanguageSelection())
    .Window(w => w
        .Size(500, 350)
        .Title("My App Setup")
        .Accent("#2563EB"))
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));

// In page classes:
public override string Title => Localize("Welcome.Title");   // Localized string lookup
public override PageResult OnNext() => PageResult.Next;       // Navigate forward
public override PageResult OnNext() => PageResult.Install;    // Trigger MSI install
public override PageResult OnNext() => PageResult.Finish;     // Close installer
```

## How to Build

```
dotnet build demo/11-custom-ui-simple/11-custom-ui-simple.csproj
```

## Notes

- Localization files are embedded as assembly resources via `<EmbeddedResource>` in the csproj, not loaded from disk at runtime.
- `AddJsonResource<T>` resolves the resource relative to the assembly containing type `T`.
- Each page is a view-model (`InstallerPage<TView>`) paired with a WPF `UserControl` view. The view is specified as the generic type parameter.
- `CanGoBack` is set to `false` on all pages in this demo to enforce a forward-only wizard flow.
