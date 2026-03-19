# WiX Burn Bundle Decompiler

Decompile WiX Burn bundle EXEs into FalkForge `BundleBuilder` C# source code.

## WiX Burn EXE Format

```
[PE Stub (burn.exe engine)]
  .wixburn section: magic 0x00F14300, dwStubSize, container sizes, BundleId
[UX Container (cabinet)]
  manifest.xml (file "0"), BootstrapperApplicationData.xml, BA DLL, resources
[Attached Container(s) (cabinet)]
  Payload files (MSI, EXE, MSP, MSU)
```

Read sequence: Parse PE section table for `.wixburn` name, read 56+ byte section, seek to `dwStubSize` for UX cabinet, extract `manifest.xml` via FDI.

## Architecture

### WixBurnAccess (PE + cabinet reading)

**`IWixBurnAccess`** -- testability interface:
- `ReadManifest()` -> `Result<XDocument>`
- `BundleId` (from .wixburn section)

**`WixBurnAccess`** -- production implementation:
- `Open(string path)` -> `Result<IWixBurnAccess>`: validates PE, finds .wixburn section, checks magic
- `ReadManifest()`: seeks to UX cabinet at `dwStubSize`, extracts via `CabinetExtractor`, parses file "0" as XML

### CabinetExtractor (FDI P/Invoke)

New class in `Compiler.Msi` alongside `CabinetBuilder`:
- `Extract(Stream cabinetStream)` -> `Dictionary<string, byte[]>`
- Uses FDI callbacks (open/read/write/close/seek) marshalling a Stream
- P/Invoke: `FDICreate`, `FDICopy`, `FDIDestroy` in `NativeMethods.Cabinet.cs`
- `FdiHandle` SafeHandle parallels existing `FciHandle`

### WixManifestMapper (XML -> BundleModel)

**Direct mappings:**
- `Registration/@DisplayName` -> Name
- `Registration/Arp/@Publisher` -> Manufacturer
- `Registration/@Version` -> Version
- `Registration/@BundleId` -> BundleId
- `Registration/@Code` -> UpgradeCode
- `Registration/@Scope` -> Scope
- `RelatedBundle` -> RelatedBundleModel[]
- `RollbackBoundary` -> RollbackBoundaryChainItem
- `Chain/MsiPackage|ExePackage|MspPackage|MsuPackage` -> BundlePackageModel
- `Container` -> ContainerModel[]

**Unmapped (emitted as C# comments + gap report):**
- Variable elements
- Search elements (registry/file/directory)
- UX / BootstrapperApplication config
- MsiProperty per-package
- ExitCode mappings beyond basics
- ApprovedExeForElevation
- BootstrapperExtension

### WixBundleDecompiler (orchestrator)

Dual-constructor (parameterless + `IWixBurnAccess` injection):
- `Decompile(string path)` -> `Result<BundleModel>`
- `DecompileToCSharp(string path)` -> `Result<string>`

Reuses `BundleCSharpEmitter` with extended comment support (preamble + unmapped features inline).

### CLI Integration

`DecompileCommand` routing for `.exe` files:
1. Try FALKBUNDLE (footer magic check)
2. Try WiX Burn (.wixburn PE section magic check)
3. Error: unsupported format

Error codes: WBD001-WBD006 (file not found, not PE, no .wixburn section, invalid magic, cabinet extraction failed, manifest not found).

## Files

| Action | File | Purpose |
|--------|------|---------|
| Create | `src/FalkForge.Decompiler/IWixBurnAccess.cs` | Testability interface |
| Create | `src/FalkForge.Decompiler/WixBurnAccess.cs` | PE parsing + cabinet extraction |
| Create | `src/FalkForge.Decompiler/WixManifestMapper.cs` | XML -> BundleModel + unmapped features |
| Create | `src/FalkForge.Decompiler/WixBundleDecompiler.cs` | Orchestrator |
| Create | `src/FalkForge.Decompiler/WixUnmappedFeature.cs` | Gap tracking record |
| Create | `src/FalkForge.Compiler.Msi/CabinetExtractor.cs` | FDI cabinet extraction |
| Create | `src/FalkForge.Compiler.Msi/Interop/FdiHandle.cs` | SafeHandle for FDI |
| Create | `tests/FalkForge.Decompiler.Tests/MockWixBurnAccess.cs` | In-memory mock |
| Create | `tests/FalkForge.Decompiler.Tests/WixManifestMapperTests.cs` | ~15 tests |
| Create | `tests/FalkForge.Decompiler.Tests/WixBundleDecompilerTests.cs` | ~8 tests |
| Edit | `src/FalkForge.Compiler.Msi/Interop/NativeMethods.Cabinet.cs` | Add FDI P/Invoke |
| Edit | `src/FalkForge.Decompiler/BundleCSharpEmitter.cs` | Preamble + unmapped comments |
| Edit | `src/FalkForge.Cli/Commands/DecompileCommand.cs` | FALKBUNDLE-first, WiX fallback |

**10 new files, 3 edited files, ~25 new tests.**

## Implementation Order

1. FDI P/Invoke + FdiHandle + CabinetExtractor
2. IWixBurnAccess + WixBurnAccess + MockWixBurnAccess
3. WixUnmappedFeature + WixManifestMapper + tests
4. BundleCSharpEmitter comment extensions
5. WixBundleDecompiler + tests
6. CLI routing update
7. CLAUDE.md update

## Verification

```bash
dotnet build FalkForge.slnx       # 0 warnings
dotnet test FalkForge.slnx        # all tests pass
forge decompile wix-bundle.exe    # produces BundleBuilder C# source
forge decompile falk-bundle.exe   # existing FALKBUNDLE path still works
forge decompile package.msi       # existing MSI path still works
```
