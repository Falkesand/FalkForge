# Demo 16: Features

Builds an installer with a nested feature tree, allowing users to selectively install components. Uses the `FeatureTree`
dialog set to present the selection UI.

## What This Demonstrates

- Top-level and nested feature definitions
- Required features that cannot be deselected (`IsRequired = true`)
- Default-selected optional features (`IsDefault = true`)
- Features that are deselected by default (`IsDefault = false`)
- `MsiDialogSet.FeatureTree` for the feature selection dialog
- `MajorUpgrade` with `AllowSameVersionUpgrades`, custom `Schedule`, and `MigrateFeatures`

## Key API Calls

```csharp
// Feature tree dialog set
package.UseDialogSet(MsiDialogSet.FeatureTree);

// Required top-level feature with a nested optional child
package.Feature("Application", app =>
{
    app.Title = "Application";
    app.Description = "Core application files";
    app.IsRequired = true;

    app.Files(f => f
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "FeatureDemo"));

    // Nested feature — selected by default
    app.Feature("Plugins", plugins =>
    {
        plugins.Title = "Plugins";
        plugins.Description = "Optional editor plugins";
        plugins.IsDefault = true;

        plugins.Files(f => f
            .Add("payload/plugins/editor.dll")
            .To(KnownFolder.ProgramFiles / "Demo" / "FeatureDemo" / "Plugins"));
    });
});

// Separate top-level feature — not selected by default
package.Feature("Documentation", docs =>
{
    docs.Title = "Documentation";
    docs.Description = "User guide and README";
    docs.IsDefault = false;

    docs.Files(f => f
        .Add("payload/docs/readme.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "FeatureDemo" / "Docs"));
});

// Major upgrade — migrate user's feature selections from the old version
package.MajorUpgrade(mu =>
{
    mu.AllowSameVersionUpgrades();
    mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
    mu.MigrateFeatures(true);
});
```

## How to Build

```bash
dotnet build demo/16-features
```

## Notes

- Files are assigned to features by calling `.Files()` inside the feature lambda. Files outside any feature belong to
  the default feature.
- Nesting `app.Feature(...)` inside another feature creates a parent-child relationship in the feature tree UI.
- `IsRequired = true` grays out the checkbox so the user cannot deselect the feature.
- `AllowSameVersionUpgrades()` permits reinstalling the same version (useful for repair/patch scenarios).
- `Schedule(AfterInstallExecute)` controls when the old product is removed. `AfterInstallExecute` installs new files
  first, then removes the old version, allowing file coexistence during the transition.
- `MigrateFeatures(true)` preserves the user's feature selections from the previous installation, so upgrades do not
  reset optional components.
