# Demo 24: Fonts

Registers a TrueType font with Windows during installation so it becomes available to all applications.

## What This Demonstrates

- Installing font files to the Windows Fonts folder using `KnownFolder.FontsFolder`
- Registering a font with `package.Font()` so Windows recognizes it
- Overriding the font's registered title with `f.Title`

## Key API Calls

```csharp
// Deploy the font file to the Fonts folder
package.Files(files => files
    .Add("payload/demofont.ttf")
    .To(KnownFolder.FontsFolder / "DemoFonts"));

// Register the font with Windows
package.Font("demofont.ttf");

// Register a font with an explicit title override
package.Font("payload/DemoSans.ttf", f =>
{
    f.Title = "Demo Sans Regular";
});
```

## How to Build

```bash
dotnet build demo/24-fonts
```

## Notes

- `package.Font()` registers the font in the MSI Font table. Windows will enumerate it and make it available system-wide after installation.
- The font file must also be included via `package.Files()`. `Font()` references the file by name.
- On uninstall, the font is unregistered and the file is removed.
- `f.Title` overrides the font's display name in the Windows Fonts folder. By default, the title is read from the font file's metadata. Use this when the embedded name is missing or needs to be customized.
