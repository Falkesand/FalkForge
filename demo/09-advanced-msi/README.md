# Demo 09: Advanced MSI

Comprehensive showcase of advanced MSI features: a complex feature tree with conditions, file operations, service control, custom actions (SetProperty + deferred DLL), two custom tables, execute sequence configuration, and media template control.

## What This Demonstrates

- `MsiDialogSet.FeatureTree` with a two-level feature tree (Core > Tools, Documentation)
- `Feature.Condition()` to disable a feature on specific OS configurations (e.g., Server Core)
- `MediaTemplate()` with custom cabinet naming, max cabinet size, compression level, and embedding
- File operations: `CreateFolder()`, `MoveFile()`, `DuplicateFile()`, `RemoveFile()` with wildcard
- `ServiceControl()` to start/stop an existing Windows service (not service install)
- Registry entries with `DWord()` and `RegistryValueType.ExpandString`, plus `RemoveRegistry()` for clean uninstall
- `CustomAction()` with `SetProperty` for type-51 property-setting actions
- `CustomAction()` with `DllFromBinary()` for deferred, elevated (`NoImpersonate()`) and rollback actions
- `Binary()` to embed a DLL into the MSI for use by custom actions
- Two `CustomTable()` definitions with String, Int32, and Int16 columns, primary keys, and nullable columns
- `ExecuteSequence()` to schedule custom actions with `After()`/`Before()` and conditional execution
- Secure property with `IsSecure = true`
- Downgrade blocking with `p.Downgrade(d => d.Block(...))`

## Key API Calls

```csharp
// Media template
p.MediaTemplate(mt => mt
    .CabinetTemplate("contoso{0}.cab")
    .MaxCabinetSizeMB(200)
    .CompressionLevel(FalkForge.CompressionLevel.High)
    .EmbedCabinet(true));

// Feature condition (disable on Server Core)
core.Feature("Tools", tools =>
{
    tools.Condition("NOT MsiNTSuitePersonal", 0);
});

// File operations
p.CreateFolder(cf => cf.Id("CreateLogsFolder").Directory("LOGSDIR"));
p.MoveFile(mf => mf.Id("MoveOldConfig").SourceDirectory("INSTALLFOLDER")
    .SourceFileName("config.old.xml").DestDirectory("INSTALLFOLDER")
    .DestFileName("config.backup.xml").AsMove());
p.DuplicateFile(df => df.Id("DuplicateConfigTemplate").FileRef("config.xml")
    .DestDirectory("INSTALLFOLDER").DestFileName("config.template.xml"));
p.RemoveFile(rf => rf.Id("RemoveLogFiles").Directory("LOGSDIR")
    .FileName("*.log").OnUninstall());

// ServiceControl (start/stop existing service)
p.ServiceControl(sc => sc.Id("StopContosoAgent").ServiceName("ContosoAgent")
    .StopOnInstall().StopOnUninstall().Wait(true));

// Deferred DLL custom action
p.Binary("ContosoActions", Path.Combine(payloadDir, "app.dll"));
p.CustomAction("ConfigureDatabase", ca =>
{
    ca.DllFromBinary("ContosoActions", "ConfigureDatabase");
    ca.Deferred();
    ca.NoImpersonate();
    ca.Condition = "NOT Installed";
});

// Execute sequence
p.ExecuteSequence(seq => seq
    .Action("RollbackDatabase").Before("ConfigureDatabase").Condition("NOT Installed")
    .Action("ConfigureDatabase").After("InstallFiles").Condition("NOT Installed"));

// Custom table
p.CustomTable(ct => ct
    .Name("ContosoDeployment")
    .Column("DeploymentId", CustomTableColumnType.String, c => c.PrimaryKey().Width(72))
    .Column("Environment", CustomTableColumnType.String, c => c.Width(50))
    .Column("Priority", CustomTableColumnType.Int32)
    .Column("Description", CustomTableColumnType.String, c => c.Nullable().Width(255))
    .Row(r => r.Set("DeploymentId", "PROD-001").Set("Environment", "Production")
        .Set("Priority", 1).Set("Description", "Primary production deployment")));
```

## How to Build

```bash
dotnet build demo/09-advanced-msi/
```

## How to Run

Produces a `.msi` file. Requires Windows with `msi.dll`.

```bash
dotnet run --project demo/09-advanced-msi/ -- -o ./output
```

## Notes

- `ServiceControl()` controls the start/stop lifecycle of an already-installed service; use `Service()` to install a new service.
- `Deferred()` + `NoImpersonate()` runs the custom action in the elevated server process rather than impersonating the calling user — required for system-level operations.
- `Rollback()` custom actions execute if the deferred action fails; schedule the rollback action `Before()` its corresponding deferred action in the execute sequence.
- `RemoveRegistry()` generates entries in the `RemoveRegistry` MSI table, which removes registry keys/values during uninstall even if the installer did not create them.
