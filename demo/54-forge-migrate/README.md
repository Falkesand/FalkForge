# Demo 54: forge migrate

Convert an existing MSI (or WiX Burn EXE) into an editable FalkForge C# project with a single command.

## What This Demonstrates

- `forge migrate <file>` — the migration command that decompiles an installer and emits a buildable FalkForge project
- The full round-trip workflow: **existing MSI → `forge migrate` → generated project → `dotnet build` → new MSI**
- What the generated project contains: `Program.cs`, a `.csproj`, extracted payload files, and a `MIGRATION-REPORT.md`
- How to use `--falkforge-src` to tell the generated project where the FalkForge source tree lives

## Who This Is For

Teams migrating off WiX, InstallShield, or other authoring tools who want to move their existing installer to FalkForge without rebuilding from scratch. `forge migrate` reads the binary MSI database (or a WiX Burn EXE manifest) and reconstructs the package definition as a FalkForge fluent API project.

## How the Demo Works

This demo pairs two things:

1. **`54-forge-migrate.csproj` / `Program.cs`** — a small FalkForge installer that builds `sample.msi` (the migration input)
2. **`migrate.ps1`** — a PowerShell script that runs the full round-trip automatically

## Step-by-Step Workflow

### Prerequisites

- .NET 10 SDK
- Windows (MSI migration calls `msi.dll`; EXE/WiX Burn migration is cross-platform)

### Option A — Run the script

```powershell
# From the demo/54-forge-migrate/ folder:
pwsh migrate.ps1
```

The script performs all three steps automatically and prints the generated project layout.

### Option B — Run the steps manually

**Step 1: Build the sample MSI**

```bash
dotnet run --project 54-forge-migrate.csproj --configuration Release -- sample.msi
```

This produces `sample.msi` in the demo folder — the installer that will be migrated.

**Step 2: Migrate the MSI**

```bash
dotnet run --project ../../src/FalkForge.Cli/FalkForge.Cli.csproj -- \
    migrate sample.msi \
    -o ./migrated \
    --falkforge-src ../../src
```

`forge migrate` flags:

| Flag | Description |
|------|-------------|
| `<file>` | Path to the installer to migrate (`.msi`, `.msm`, or `.exe`) |
| `-o` / `--output` | Output directory for the generated project (default: `<filename>-migrated`) |
| `--falkforge-src` | Path to the FalkForge `src/` directory for the `ProjectReference` in the generated `.csproj` |

**Step 3: Build the generated project**

```bash
dotnet build migrated/
```

If the build succeeds, the round-trip is complete. The generated project is a fully buildable FalkForge installer.

## What the Generated Project Contains

After migration, the output directory contains:

```
migrated/
  ForgeMigrateSample.csproj   # Generated project with ProjectReferences to FalkForge.Core + Compiler.Msi
  Program.cs                  # Reconstructed fluent API installer definition
  payload/                    # Extracted payload files from the original MSI cabinet
  MIGRATION-REPORT.md         # Summary of what was mapped, what was skipped, and any unmapped features
```

`MIGRATION-REPORT.md` lists any MSI features that could not be automatically mapped (custom actions, complex sequences, etc.) so you know exactly what to review and complete by hand.

## WiX Burn EXE Support

`forge migrate` also accepts WiX Burn bundles (`.exe`). On Windows it uses PE parsing + UX cabinet extraction to read the Burn manifest and reconstruct a `BundleBuilder`-based project. The same `-o` and `--falkforge-src` flags apply.

```bash
forge migrate my-installer.exe -o ./migrated --falkforge-src ../../src
```

## Key API Calls in the Generated Program.cs

The generated `Program.cs` will contain fluent API calls like these, reconstructed from the original MSI:

```csharp
return Installer.Build(args, package =>
{
    package.Name = "Forge Migrate Sample";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Files(files => files
        .Add("payload/readme.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "ForgeMigrateSample"));
}, new MsiCompiler());
```

## How to Build the Sample Installer Directly

```bash
dotnet build demo/54-forge-migrate
```

## Notes

- `migrated/`, `sample.msi`, `bin/`, and `obj/` are excluded from git via `.gitignore` — generated output is not committed.
- The `--falkforge-src` flag is required when the FalkForge source tree cannot be auto-discovered by walking up from the CLI binary location. In this demo the relative path `../../src` resolves correctly when run from the demo folder.
- Migration accuracy depends on the complexity of the source MSI. Simple file-deploy installers round-trip cleanly; installers with complex custom action sequences may have unmapped entries listed in `MIGRATION-REPORT.md`.
