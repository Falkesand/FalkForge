# UI Localization Design

## Goal

Add auto-locale localization to both the custom UI framework (runtime) and MSI dialog templates (compile-time). End-users can optionally switch language during installation. Ship Swedish and English for all demos.

## Architecture

Two systems, unified by the same JSON format and culture fallback logic:

1. **Custom UI runtime localization** — `InstallerPage.Localize("key")` resolves strings at runtime from embedded JSON resources, auto-detecting OS culture.
2. **MSI dialog template localization** — Templates use `!(loc.Dialog.X)` references resolved at compile time by the existing `LocalizedStringResolver`.

## Custom UI Runtime Localization

### Registration

```csharp
InstallerApp.Run(args, ui => ui
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<Program>("lang/en-US.json")
        .AddJsonResource<Program>("lang/sv-SE.json")
        .AllowLanguageSelection())   // opt-in language dropdown
    .Page<WelcomePage>()
);
```

`AddJsonResource<T>(path)` loads an embedded resource from the assembly containing `T`. The path maps to the embedded resource name.

### Usage from Pages

```csharp
public override string Title => Localize("WelcomePage.Title");
public string Subtitle => Localize("WelcomePage.Subtitle");
```

`Localize(key)` returns the resolved string for the current culture. Falls back through the culture chain (e.g., sv-SE -> sv -> en-US). If no localization is configured, returns the key itself — graceful degradation.

### Auto-detection

On startup, the framework reads `CultureInfo.CurrentUICulture`, builds a fallback chain, and selects the best match from available cultures. Uses the same logic as `CultureFallbackChain` in `FalkForge.Localization`.

### JSON Format

Same format as existing `FalkForge.Localization`:

```json
{
  "WelcomePage.Title": "Welcome",
  "WelcomePage.Subtitle": "Welcome to the installer",
  "LicensePage.Title": "License Agreement"
}
```

### Runtime Language Switching

When `AllowLanguageSelection()` is enabled:

1. A language dropdown appears in the installer window chrome (top-right, near banner area)
2. Shows available cultures by native display name (e.g., "English", "Svenska")
3. When the user picks a language:
   - `UiStringResolver.CurrentCulture` is updated
   - `UiStringResolver` raises `CultureChanged` event
   - `CustomShellViewModel` catches it
   - Calls `InstallerPage.NotifyCultureChanged()` which fires `OnPropertyChanged("")` (WPF all-properties-changed convention)
   - All bindings re-evaluate, calling `Localize()` with the new culture

Cost is negligible — one event, one blanket PropertyChanged, only on explicit user action.

### Components (all in FalkForge.Ui)

| Class | Responsibility |
|-------|---------------|
| `UiLocalizationBuilder` | Fluent config: AddJsonResource, DefaultCulture, AllowLanguageSelection, DetectCulture |
| `UiLocalizationConfig` | Internal record holding resolved config |
| `UiStringResolver` | Loaded dictionaries + culture fallback resolution |
| `LanguageSelectorControl` | WPF UserControl for language dropdown (only when AllowLanguageSelection) |

`InstallerPage` gains:
- `internal UiStringResolver? _stringResolver` — set by framework
- `protected string Localize(string key)` — delegates to `_stringResolver?.Resolve(key) ?? key`
- `internal void NotifyCultureChanged()` — fires `OnPropertyChanged("")`

## MSI Dialog Template Localization

### Current State

Templates in `FalkForge.Compiler.Msi/UI/Templates/` emit hardcoded English:
```csharp
new MsiControlModel { Text = "&Next >" }
```

### Change

Replace with localization references:
```csharp
new MsiControlModel { Text = "!(loc.Dialog.Next)" }
```

### Built-in Localization Files

Shipped as embedded resources in `FalkForge.Compiler.Msi`:
```
src/FalkForge.Compiler.Msi/Localization/en-US.json
src/FalkForge.Compiler.Msi/Localization/sv-SE.json
```

Cover ~30-40 strings across all 5 dialog templates (buttons, titles, descriptions, labels).

### API

```csharp
.Localization(loc => loc
    .AddBuiltInCultures()   // loads en-US + sv-SE from Compiler.Msi assembly
    .DetectCulture())       // picks OS culture at compile time
```

`AddBuiltInCultures()` loads embedded resources from `FalkForge.Compiler.Msi`. The existing `LocalizedStringResolver` resolves `!(loc.Dialog.X)` references during MSI compilation.

`DetectCulture()` sets active culture from `CultureInfo.CurrentUICulture` with fallback chain.

User-provided localization files merge on top of built-in strings (user strings override built-in).

### MSI Constraint

MSI packages are single-language. The culture is selected at compile time and baked into the MSI database. No runtime switching for MSI — this is by Windows Installer design.

## Demo Updates

### Custom UI Demos (11, 12, MAS)

Each gets:
- `lang/en-US.json` and `lang/sv-SE.json` as embedded resources
- All user-visible strings extracted to localization keys
- `AllowLanguageSelection()` enabled to showcase the feature

| Demo | Pages | Approximate Strings |
|------|-------|-------------------|
| 11-custom-ui-simple | 3 (Welcome, Install, Complete) | ~10 |
| 12-custom-ui-vstyle | 4 (Product, Workloads, Progress, Complete) | ~20 |
| MAS | ~15 pages | ~80+ |

### MSI Script Demos (01-10)

Each gets `.Localization(loc => loc.AddBuiltInCultures().DetectCulture())` added to the PackageBuilder chain. No per-demo JSON files — built-in template strings cover all dialog text.

Demo 08 (localization) keeps its existing custom culture files and additionally uses `AddBuiltInCultures()` for template strings.

### Translated Strings (Swedish)

- Buttons: Next->Nästa, Back->Tillbaka, Cancel->Avbryt, Browse->Bläddra, Install->Installera, Finish->Slutför
- Page titles and descriptions
- License text (demo placeholder)
- Status messages, warnings, labels
- MAS-specific: database, ODBC, service account labels
