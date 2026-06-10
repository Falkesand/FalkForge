# Demo 04: Dev Toolkit

A developer tools suite installer using the Mondo dialog set (Welcome + License + InstallDir + Features + Progress + Exit). Shows deeply nested feature hierarchies, file associations, custom actions, and downgrade blocking.

## What This Demonstrates

- `MsiDialogSet.Mondo` for the full wizard experience including license and install directory
- Nested features: Editor contains a child EditorPlugins feature
- `FileAssociation()` for registering `.falk` and `.fks` file extensions with a shell verb
- `EnvironmentVariableAction.Set` for `FALK_HOME` and `EnvironmentVariableAction.Append` with separator for `PATH`
- Registry entries recording compiler and debugger paths
- `CustomAction()` with `SetProperty` to set a runtime MSI property (`FALK_VERSION`)
- Optional feature (`IsDefault = false`) for the Debugger
- Downgrade blocking with `p.Downgrade(d => d.Block(...))`
- `LicenseFile` on the package for RTF license display

## Key API Calls

```csharp
// Mondo dialog (license + install dir + features)
p.UseDialogSet(MsiDialogSet.Mondo);
p.LicenseFile = "payload/license.rtf";

// Nested feature
p.Feature("Editor", f =>
{
    f.IsDefault = true;
    f.Feature("EditorPlugins", child =>
    {
        child.Title = "Editor Plugins";
        child.IsDefault = true;
    });
});

// File association with shell verb
p.FileAssociation(".falk", fa =>
{
    fa.ProgId("FalkTech.FalkProject");
    fa.Description = "Falk Project File";
    fa.Verb(ShellVerb.Open, "\"%1\"");
});

// Environment variable: set
p.EnvironmentVariable("FALK_HOME", "[INSTALLFOLDER]", ev =>
{
    ev.IsSystem = true;
    ev.Action = EnvironmentVariableAction.Set;
});

// Environment variable: append to PATH
p.EnvironmentVariable("PATH", "[INSTALLFOLDER]bin", ev =>
{
    ev.IsSystem = true;
    ev.Action = EnvironmentVariableAction.Append;
    ev.Separator = ";";
});

// SetProperty custom action
p.CustomAction("SetFalkVersion", ca =>
{
    ca.SetProperty("FALK_VERSION", "5.0.0");
    ca.After = "InstallValidate";
});

// Block downgrades
p.Downgrade(d => d.Block("A newer version of Falk Developer Toolkit is already installed."));
```

## How to Build

```bash
dotnet build demo/04-dev-toolkit/
```

## How to Run

Produces a `.msi` file. Requires Windows with `msi.dll`.

```bash
dotnet run --project demo/04-dev-toolkit/ -- -o ./output
```

## Notes

- The `Mondo` dialog set includes a license page; `LicenseFile` must point to a valid RTF file.
- `EnvironmentVariableAction.Append` with `Separator = ";"` extends an existing variable (such as `PATH`) rather than replacing it.
- `IsDefault = false` on the Debugger feature means it is listed in the feature tree but unchecked by default; the user must opt in.
