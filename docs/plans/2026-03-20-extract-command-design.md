# Extract Command Design

**Goal:** Add a `forge extract` CLI command and bundle `--extract` self-extraction flag that extract MSI cabinet files and bundle payloads to disk without installing.

**Architecture:** Two entry points (CLI tool + bundle self-extraction) sharing the same extraction logic. MSI extraction uses MsiDatabase for path resolution + CabinetExtractor for decompression. Bundle extraction uses BundleReader for payload access.

---

## CLI: `forge extract`

```
forge extract <file> [options]

Arguments:
  <file>              MSI, MSM, or EXE bundle to extract

Options:
  -o, --output <dir>  Output directory (required for extraction)
  --list              List packages without extracting
  --package <name>    Extract specific package(s) (repeatable, bundle only)
```

### Examples

```bash
# Extract MSI files to mirrored install paths
forge extract myapp.msi -o D:\Temp\myapp

# List packages in a bundle
forge extract suite.exe --list

# Extract all packages from bundle (creates subfolders)
forge extract suite.exe -o D:\Temp

# Extract specific packages from bundle
forge extract suite.exe -o D:\Temp --package ServerMsi --package ClientMsi
```

## Bundle Self-Extraction

```bash
# List available packages
myapp.exe --extract-list

# Extract all packages
myapp.exe --extract D:\Temp

# Extract specific packages
myapp.exe --extract D:\Temp --package ServerMsi --package ClientMsi
```

Bypasses UI/engine/elevation entirely. No install, no detection, no planning. Just extraction and exit.

## Behavior Matrix

| Input | Action | Output Structure |
|-------|--------|-----------------|
| `.msi` / `.msm` | Read File/Component/Directory tables, extract cabinets | `<output>/<install-path-tree>/` mirroring install directories |
| `.exe` bundle, no `--package` | Extract all payloads | `<output>/<PackageId>/` subfolder per package |
| `.exe` bundle, with `--package` | Extract named packages only | `<output>/<PackageId>/` for selected packages |
| `--list` / `--extract-list` | Print package table | Console output: PackageId, Type, Size |

## MSI Extraction Flow

1. Open MSI via `MsiDatabase.Open(path, readOnly: true)`
2. Query Directory table → build directory tree with `DirectoryResolver`
3. Query File + Component tables → map each file to its install directory
4. Extract cabinets via `CabinetExtractor.Extract()`
5. Write files to `<output>/<resolved-directory-path>/<filename>`

## Bundle Extraction Flow

1. Read bundle via `BundleReader.Extract(bundlePath)` → `BundleContent`
2. Parse manifest for package metadata (PackageId, Type, Size)
3. For `--list`: print table and exit
4. For each package to extract:
   a. `BundleReader.ExtractPayload(bundlePath, tocEntry)` → decompressed bytes
   b. Write to `<output>/<PackageId>/<original-filename>`
5. Verify SHA-256 hash matches

## Bundle Self-Extraction Flow

1. Engine argument parser detects `--extract` or `--extract-list`
2. Bypass all engine phases (no Initializing → Detecting → etc.)
3. Read own executable as bundle source (`Process.GetCurrentProcess().MainModule.FileName`)
4. Run same extraction logic as CLI
5. Exit with code 0 (success) or 2 (error)

## Console Output Format

```
Extracting suite.exe...
  ServerMsi (msi, 4.2 MB) → D:\Temp\ServerMsi\
  ClientMsi (msi, 2.1 MB) → D:\Temp\ClientMsi\
  Prerequisites (exe, 12.8 MB) → D:\Temp\Prerequisites\
Extracted 3 packages to D:\Temp\
```

For `--list`:
```
Packages in suite.exe:
  ServerMsi          msi    4.2 MB
  ClientMsi          msi    2.1 MB
  Prerequisites      exe   12.8 MB
```

## Existing Building Blocks

| Component | Location | Status |
|-----------|----------|--------|
| `BundleReader.Extract()` | Engine.Protocol/Bundle/BundleReader.cs | Ready |
| `BundleReader.ExtractPayload()` | Engine.Protocol/Bundle/BundleReader.cs | Ready |
| `CabinetExtractor.Extract()` | Compiler.Msi/CabinetExtractor.cs | Ready |
| `MsiDatabase.Open()` + `QueryRows()` | Compiler.Msi/MsiDatabase.cs | Ready |
| `DirectoryResolver` | Decompiler/DirectoryResolver.cs | Ready |
| CLI command registration | Cli/Program.cs | Extend |
| Engine argument parser | Engine/EngineHost.cs | Extend |

## New Code Required

1. **`ExtractCommand.cs`** — CLI command (Spectre.Console) with settings class
2. **`ExtractSettings.cs`** — Command settings (file, output, list, package names)
3. **`MsiExtractor.cs`** — Orchestrates MSI extraction (MsiDatabase + DirectoryResolver + CabinetExtractor)
4. **`BundleExtractorService.cs`** — Orchestrates bundle extraction (BundleReader + file I/O)
5. **Engine `--extract` handling** — Early exit path in EngineHost argument parsing

## Error Handling

- File not found → exit code 2, clear message
- Invalid format (not MSI/bundle) → exit code 2
- Output directory creation failure → exit code 2
- Cabinet extraction failure → exit code 2, report which cabinet failed
- SHA-256 mismatch on bundle payload → exit code 2, report which payload
- Package name not found in bundle → exit code 1, list available packages
