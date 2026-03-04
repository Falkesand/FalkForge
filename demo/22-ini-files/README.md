# Demo 22: INI Files

Writes configuration entries to an INI file during installation. Demonstrates creating key-value pairs in specific
sections of an INI file.

## What This Demonstrates

- Writing entries to an INI file with `package.IniFile()`
- Targeting specific sections and keys
- Using MSI directory properties as values (resolved at install time)
- The `IniFileAction.CreateEntry` action type

## Key API Calls

```csharp
// Write InstallPath to [General] section
package.IniFile("settings.ini", ini =>
{
    ini.Section("General");
    ini.Key("InstallPath");
    ini.Value(@"[ProgramFilesFolder]Demo\IniDemo");
    ini.Action(IniFileAction.CreateEntry);
});

// Write Version to [General] section
package.IniFile("settings.ini", ini =>
{
    ini.Section("General");
    ini.Key("Version");
    ini.Value("1.0.0");
    ini.Action(IniFileAction.CreateEntry);
});
```

## How to Build

```bash
dotnet build demo/22-ini-files
```

## Notes

- `IniFileAction.CreateEntry` creates the key if it does not exist but does not overwrite an existing value. Other
  action types are available for overwriting or removing entries.
- The INI file must be included in the `Files()` section so it exists on disk before entries are written.
- MSI property references like `[ProgramFilesFolder]` are resolved to actual paths at install time.
