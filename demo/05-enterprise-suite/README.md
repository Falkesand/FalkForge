# Demo 05: Enterprise Suite

The largest MSI demo. A full enterprise IDE installer with 8 top-level features (including 7 nested sub-features), 71 payload files, shortcuts, a Windows service, file associations, environment variables, custom tables, a custom action, font registration, and launch conditions.

## What This Demonstrates

- `MsiDialogSet.Advanced` dialog set with `CabinetThreads(4)` for parallel cabinet building
- 8 top-level features with deeply nested sub-features (e.g., WebTools > WebServer, WebFrameworks, BrowserTools)
- `InstallScope.PerMachine` and `ProcessorArchitecture.X64`
- Multiple shortcuts on Desktop and Start Menu via `OnDesktop()` and `OnStartMenu()`
- Windows service (`ApexWebServer`) with `ServiceStartMode.Manual` and `ServiceAccount.LocalService`
- File associations for `.aproj` and `.asln` extensions
- Multiple system environment variables including PATH append
- `CustomTable()` with String and Int32 columns, primary key, and 5 data rows
- `CustomAction()` with `SetProperty` to set `APEX_VERSION` at `CostFinalize`
- Font registration with `Font()` and `Title` override for `ApexMono.ttf`
- Package properties: `ARPPRODUCTICON`, `ALLUSERS`
- Major upgrade with `AllowSameVersionUpgrades()` and `Schedule(AfterInstallValidate)`, plus downgrade blocking
- Launch conditions requiring 64-bit OS and administrator privileges

## Key API Calls

```csharp
// Advanced dialog + parallel cabinet build
pkg.UseDialogSet(MsiDialogSet.Advanced);
pkg.CabinetThreads(4);

// Nested features
pkg.Feature("WebTools", web =>
{
    web.Feature("WebServer", ws => { ws.IsDefault = true; /* ... */ });
    web.Feature("WebFrameworks", wf => { /* ... */ });
});

// Service
pkg.Service("ApexWebServer", svc =>
{
    svc.Executable = "[INSTALLFOLDER]Web\\Server\\webserver.exe";
    svc.StartMode = ServiceStartMode.Manual;
    svc.Account = ServiceAccount.LocalService;
});

// Custom table
pkg.CustomTable(ct =>
{
    ct.Name("ApexComponents");
    ct.Column("ComponentId", CustomTableColumnType.String, c => c.PrimaryKey().Width(72));
    ct.Column("Category", CustomTableColumnType.String, c => c.Width(64));
    ct.Column("Priority", CustomTableColumnType.Int32);
    ct.Row(r => r.Set("ComponentId", "apex.core.dll").Set("Category", "IDE").Set("Priority", 1));
});

// Font registration
pkg.Font(Path.Combine(payloadDir, "fonts", "ApexMono.ttf"), f => { f.Title = "Apex Mono"; });

// Major upgrade + downgrade block
pkg.MajorUpgrade(mu =>
{
    mu.AllowSameVersionUpgrades();
    mu.Schedule(RemoveExistingProductsSchedule.AfterInstallValidate);
});
pkg.Downgrade(d => d.Block("A newer version of Apex Enterprise Suite is already installed."));

// Launch conditions
pkg.Require("Privileged", "Administrator privileges are required.");
pkg.Require("VersionNT64", "A 64-bit operating system is required.");
```

## How to Build

```bash
dotnet build demo/05-enterprise-suite/
```

## How to Run

Produces a `.msi` file. Requires Windows with `msi.dll`.

```bash
dotnet run --project demo/05-enterprise-suite/ -- -o ./output
```

## Notes

- `CabinetThreads(4)` uses `ParallelCabinetBuilder` internally to compress payload files in parallel, significantly reducing build time for large demos.
- `Downgrade(d => d.Block(...))` generates an `Upgrade` table entry that blocks installation when a newer version is already present, with a custom error message.
- The font file `ApexMono.ttf` is registered in the system font directory via the `Font` table; `Title` overrides the display name shown in the Fonts control panel.
