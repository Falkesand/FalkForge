# Demo 01: Hello World

The simplest possible FalkForge installer. Installs a single text file into Program Files using the minimal dialog set and built-in localization support.

## What This Demonstrates

- Minimal `Installer.Build` setup with name, manufacturer, and version
- Using `MsiDialogSet.Minimal` for a streamlined install experience
- Adding built-in culture support with automatic culture detection
- Deploying a single file to a `KnownFolder.ProgramFiles` subdirectory
- Configuring cabinet files with `MediaTemplate` (naming, compression, embedding)
- Enabling deterministic/reproducible builds with `Reproducible()`
- Enabling Windows Restart Manager support for graceful file-in-use handling

## Key API Calls

```csharp
// Core package metadata
package.Name = "Hello World";
package.Manufacturer = "Demo";
package.Version = new Version(1, 0, 0);

// Dialog set — Minimal skips feature selection and install-dir prompts
package.UseDialogSet(MsiDialogSet.Minimal);

// Localization — adds all built-in cultures, defaults to en-US, auto-detects user locale
package.Localization(loc => loc
    .AddBuiltInCultures()
    .DefaultCulture("en-US")
    .DetectCulture());

// Cabinet file settings — naming template, compression level, embedding
package.MediaTemplate(mt =>
{
    mt.CabinetTemplate("data{0}.cab");
    mt.CompressionLevel(CompressionLevel.High);
    mt.EmbedCabinet(true);
});

// Enable deterministic builds (same source → identical MSI output)
package.Reproducible();

// Enable Windows Restart Manager — gracefully close files-in-use during install
package.EnableRestartManagerSupport();

// Files — add a single file and set its install directory
package.Files(files => files
    .Add("payload/hello.txt")
    .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));
```

## How to Build

```bash
dotnet build demo/01-hello-world
```

## Notes

- The `KnownFolder.ProgramFiles / "Demo" / "HelloWorld"` syntax builds the install directory path using the `/` operator for readability.
- `DetectCulture()` automatically selects the MSI language matching the user's OS locale.
- `MediaTemplate` controls how payload files are packaged into cabinet (.cab) files. `EmbedCabinet(true)` stores cabinets inside the MSI itself.
- `Reproducible()` ensures that building the same source twice produces a byte-identical MSI, useful for build verification and supply-chain integrity.
- `EnableRestartManagerSupport()` allows Windows to gracefully close and restart applications that have files in use during installation, avoiding forced reboots.
