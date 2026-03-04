# Demo 08: Localization

A multi-language MSI installer demonstrating the FalkForge localization system with JSON-based string files, culture
fallback chains, inline culture overrides, and localized string references in package metadata.

## What This Demonstrates

- JSON localization files loaded from disk with `AddJsonFile()`
- Built-in culture support with `AddBuiltInCultures()` for standard MSI dialog strings
- Default culture declaration with `DefaultCulture("en-US")`
- Culture fallback chain: specific -> parent -> default (e.g., `de-AT` -> `de` -> `en-US`)
- Inline culture definitions with `AddCulture()` for partial overrides
- Localized string references using `!(loc.StringId)` syntax in any string property
- Localized feature titles, descriptions, shortcuts, and package metadata
- `MsiDialogSet.FeatureTree` for feature selection UI with localized labels

## Key API Calls

```csharp
package.Localization(loc =>
{
    loc.AddBuiltInCultures();                                      // Standard MSI dialog strings
    loc.AddJsonFile(Path.Combine(langDir, "strings.en-US.json"));  // Full language file
    loc.AddJsonFile(Path.Combine(langDir, "strings.de.json"));
    loc.AddJsonFile(Path.Combine(langDir, "strings.fr.json"));

    // Partial override -- only one string, rest falls back to "de" then "en-US"
    loc.AddCulture("de-AT", new Dictionary<string, string>
    {
        ["FinishMessage"] = "Installation abgeschlossen. Bitte auf Fertigstellen klicken."
    });

    loc.DefaultCulture("en-US");
});

// Use !(loc.StringId) syntax in any string property
package.Name = "!(loc.ProductName)";
package.Description = "!(loc.WelcomeMessage)";

package.Feature("Core", f =>
{
    f.Title = "!(loc.FeatureCore)";
    f.Description = "!(loc.FeatureCore)";
});

package.Shortcut("!(loc.ProductName)", "app.exe")
    .WithDescription("!(loc.WelcomeMessage)")
    .OnStartMenu("Falk Software");
```

## How to Build

```
dotnet build demo/08-localization/08-localization.csproj
```

## Notes

- JSON localization files follow the naming convention `name.culture.json` (e.g., `strings.en-US.json`,
  `strings.de.json`).
- The `!(loc.StringId)` syntax is resolved at compile time when generating the MSI. The MSI contains separate transform
  tables for each culture.
- Culture fallback is hierarchical: `de-AT` first checks its own strings, then falls back to `de`, then to `en-US`. This
  means you only need to override the strings that differ in regional variants.
- `AddBuiltInCultures()` loads the standard FalkForge translations for MSI dialog elements (buttons, labels). Your JSON
  files only need to contain your application-specific strings.
- `DetectCulture()` is not used in this demo (unlike the UI demos), because MSI culture selection happens at compile
  time, not at runtime.
