# MSIX Compiler Design — FalkForge.Compiler.Msix

> **Date:** 2026-03-06
> **Status:** Approved
> **Approach:** COM Interop with IAppxFactory (Windows IAppxPackaging API)

## Overview

Add MSIX package output support to FalkForge via a new `FalkForge.Compiler.Msix` project. Uses Windows' built-in `IAppxFactory` COM API for package creation, avoiding reimplementation of ZIP/block map correlation and the proprietary `AppxSignature.p7x` format.

MSIX gets its own dedicated `MsixModel` and `MsixBuilder` (separate from `PackageModel`/`BundleModel`) because MSIX has fundamentally different semantics: containerized, declarative, no custom actions at install time.

## Project Structure

```
src/FalkForge.Compiler.Msix/
├── FalkForge.Compiler.Msix.csproj
├── MsixCompiler.cs                 # ICompiler implementation (7-step pipeline)
├── MsixValidator.cs                # MSIX-specific validation rules
├── MsixModel.cs                    # Immutable package model
├── MsixBundleModel.cs              # Multi-arch bundle model
├── MsixBundleCompiler.cs           # IAppxBundleFactory wrapper
├── Builders/
│   ├── MsixBuilder.cs              # Fluent API → MsixModel
│   └── MsixBundleBuilder.cs        # Fluent API → MsixBundleModel
├── Manifest/
│   ├── AppxManifestGenerator.cs    # MsixModel → AppxManifest.xml
│   └── AppInstallerGenerator.cs    # Auto-update .appinstaller XML
├── Packaging/
│   ├── AppxPackageWriter.cs        # COM interop wrapper
│   └── VfsMapper.cs                # Install paths → VFS folder structure
├── Registry/
│   └── RegistryHiveBuilder.cs      # Create Registry.dat via P/Invoke
└── Interop/
    ├── IAppxFactory.cs             # COM interface definitions
    ├── IAppxPackageWriter.cs       # COM interface definitions
    ├── IAppxBundleFactory.cs       # COM interface definitions
    └── AppxGuids.cs                # CLSIDs and IIDs

tests/FalkForge.Compiler.Msix.Tests/
├── FalkForge.Compiler.Msix.Tests.csproj
├── MsixValidatorTests.cs
├── AppxManifestGeneratorTests.cs
├── VfsMapperTests.cs
├── RegistryHiveBuilderTests.cs
├── MsixCompilerIntegrationTests.cs
└── MsixBundleCompilerTests.cs
```

## MsixModel

```csharp
public sealed class MsixModel
{
    // Identity (maps to <Identity>)
    public required string Name { get; init; }              // "Publisher.AppName" format
    public required string Publisher { get; init; }         // "CN=..." must match cert
    public required Version Version { get; init; }          // Major.Minor.Build.Revision
    public ProcessorArchitecture Architecture { get; init; }

    // Properties (maps to <Properties>)
    public required string DisplayName { get; init; }
    public required string PublisherDisplayName { get; init; }
    public string? Description { get; init; }
    public string? LogoPath { get; init; }

    // Application entry points (at least one required)
    public required IReadOnlyList<MsixApplication> Applications { get; init; }

    // Content
    public IReadOnlyList<FileEntryModel> Files { get; init; } = [];
    public IReadOnlyList<MsixRegistryEntry> RegistryEntries { get; init; } = [];
    public IReadOnlyList<ShortcutModel> Shortcuts { get; init; } = [];

    // Capabilities
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> RestrictedCapabilities { get; init; } = [];

    // Dependencies
    public string MinWindowsVersion { get; init; } = "10.0.17763.0";
    public string? MaxVersionTested { get; init; }
    public IReadOnlyList<MsixPackageDependency> Dependencies { get; init; } = [];

    // Extensions (file associations, protocols, COM, services, etc.)
    public IReadOnlyList<MsixExtension> Extensions { get; init; } = [];

    // VFS mapping
    public VfsMappingMode VfsMapping { get; init; } = VfsMappingMode.Auto;
    public IReadOnlyList<VfsOverride> VfsOverrides { get; init; } = [];

    // Cross-cutting
    public InstallScope Scope { get; init; }
    public SigningOptions? Signing { get; init; }
    public SbomOptions? SbomOptions { get; init; }

    // Auto-update
    public MsixUpdateSettings? UpdateSettings { get; init; }
}
```

### Supporting Types

```csharp
public sealed class MsixApplication
{
    public required string Id { get; init; }
    public required string Executable { get; init; }
    public string EntryPoint { get; init; } = "Windows.FullTrustApplication";
    public string TrustLevel { get; init; } = "mediumIL";
    public string RuntimeBehavior { get; init; } = "packagedClassicApp";
    public required MsixVisualElements VisualElements { get; init; }
}

public sealed class MsixVisualElements
{
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public string BackgroundColor { get; init; } = "transparent";
    public string? Square150x150Logo { get; init; }
    public string? Square44x44Logo { get; init; }
    public string? Wide310x150Logo { get; init; }
}

// Union type for manifest extensions
public abstract class MsixExtension { }
public sealed class MsixFileTypeAssociation : MsixExtension { ... }
public sealed class MsixProtocolHandler : MsixExtension { ... }
public sealed class MsixComServer : MsixExtension { ... }
public sealed class MsixServiceDeclaration : MsixExtension { ... }
public sealed class MsixStartupTask : MsixExtension { ... }
public sealed class MsixFirewallRule : MsixExtension { ... }

public enum VfsMappingMode { Auto, Manual }

public sealed class VfsOverride
{
    public required string SourcePath { get; init; }
    public required string VfsFolder { get; init; }   // e.g. "ProgramFilesX64"
    public string? SubPath { get; init; }
}

public sealed class MsixUpdateSettings
{
    public required string AppInstallerUri { get; init; }
    public int HoursBetweenUpdateChecks { get; init; } = 24;
    public bool ShowPrompt { get; init; }
    public bool UpdateBlocksActivation { get; init; }
    public bool ForceUpdateFromAnyVersion { get; init; }
    public bool AutomaticBackgroundTask { get; init; }
}

public sealed class MsixPackageDependency
{
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required string MinVersion { get; init; }
}

public sealed class MsixRegistryEntry
{
    public required string Root { get; init; }       // "HKLM" or "HKCU"
    public required string Key { get; init; }
    public string? ValueName { get; init; }
    public string? Value { get; init; }
    public string ValueType { get; init; } = "REG_SZ";
}
```

## MsixBundleModel

```csharp
public sealed class MsixBundleModel
{
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required Version Version { get; init; }
    public IReadOnlyList<MsixBundlePackage> Packages { get; init; } = [];
    public SigningOptions? Signing { get; init; }
}

public sealed class MsixBundlePackage
{
    public required string FilePath { get; init; }
    public ProcessorArchitecture Architecture { get; init; }
    public string? ResourceId { get; init; }
}
```

## VFS Mapping

### Auto Mode

`VfsMapper` resolves MSI-style install directory tokens to MSIX VFS folders:

| Install Directory Token | VFS Folder |
|------------------------|------------|
| `[ProgramFilesFolder]` or `[ProgramFiles64Folder]` | `VFS/ProgramFilesX64` |
| `[ProgramFilesFolder]` (x86 package) | `VFS/ProgramFilesX86` |
| `[CommonFilesFolder]` / `[CommonFiles64Folder]` | `VFS/ProgramFilesCommonX64` |
| `[SystemFolder]` / `[System64Folder]` | `VFS/SystemX64` |
| `[WindowsFolder]` | `VFS/Windows` |
| `[CommonAppDataFolder]` | `VFS/CommonAppData` |
| `[AppDataFolder]` | `VFS/AppData` |
| `[LocalAppDataFolder]` | `VFS/LocalAppData` |
| `[FontsFolder]` | `VFS/Fonts` |
| (no recognized token) | Package root (application directory) |

### Manual Mode

Only explicit `VfsOverrides` are used. Files not covered by an override go to the package root.

## Compiler Pipeline (7 Steps)

```
Step 1: Validate MsixModel (MsixValidator)
  - Publisher matches "CN=..." format
  - At least one Application defined
  - Capabilities are valid known strings
  - MinWindowsVersion is valid version string
  - Source files exist on disk
  - No unsupported features (custom actions, drivers, system env vars)

Step 2: Resolve VFS layout (VfsMapper)
  - Auto: map install paths → VFS folders using table above
  - Manual: use VfsOverrides only
  - Output: List<(string sourcePath, string packageRelativePath)>

Step 3: Generate AppxManifest.xml (AppxManifestGenerator)
  - Emit <Identity Name Publisher Version ProcessorArchitecture>
  - Emit <Properties> with DisplayName, Logo, Description
  - Emit <Dependencies> with TargetDeviceFamily and PackageDependency
  - Emit <Capabilities> and <rescap:Capability>
  - Emit <Applications> with <Application> and <uap:VisualElements>
  - Emit <Extensions> for file associations, protocols, COM, services
  - Output: XDocument or MemoryStream

Step 4: Build Registry.dat (RegistryHiveBuilder)
  - Only if RegistryEntries is non-empty
  - Create temp hive file
  - Load hive via RegLoadKey P/Invoke
  - Create keys/values via RegCreateKeyEx / RegSetValueEx
  - Save via RegSaveKeyEx (standard hive format)
  - Unload hive
  - Output: byte[] or file path

Step 5: Create MSIX package (AppxPackageWriter)
  - CoCreateInstance(CLSID_AppxFactory) → IAppxFactory
  - factory.CreatePackageWriter(outputStream, settings) → IAppxPackageWriter
  - For each resolved file: writer.AddPayloadFile(relativePath, contentType, compression, inputStream)
  - If Registry.dat exists: add as payload
  - If logo/assets exist: add under Assets/
  - writer.Close(manifestStream) — auto-generates BlockMap and ContentTypes
  - Output: .msix file on disk

Step 6: Sign package (CodeSigner)
  - Reuse existing CodeSigner with SignTool.exe
  - MSIX MUST be signed — fail if Signing is null
  - signtool sign /fd SHA256 /f cert.pfx /p pw /tr timestamp output.msix

Step 7: Generate .appinstaller (optional)
  - Only if UpdateSettings is configured
  - Generate XML per AppInstaller schema
  - Write alongside .msix
  - Output: .appinstaller file
```

## What MSIX Rejects (Validation)

These produce `Result.Failure` at validation:
- Custom actions (MSIX is purely declarative)
- Kernel-mode drivers
- System-wide environment variables (container-scoped only)
- In-process COM servers (only out-of-process supported)
- Missing signing configuration (MSIX must be signed)

These produce warnings:
- Services with complex dependencies
- Large file counts (may impact install time due to block map)

## Integration Points

### CLI
- Add `--format msix` flag to `build` command
- Auto-detect from output file extension (`.msix`)
- New `MsixBundleBuilder` for `--format msixbundle`

### Studio
- Add "msix" project type alongside "msi" and "bundle"
- Show MSIX-specific editors: Capabilities, Applications, Visual Assets
- Hide inapplicable editors: Custom Actions
- `StudioBuildService.BuildMsixModel()` method

### Signing
- Reuse `SigningOptions` model and `CodeSigner` class
- Same `signtool.exe` — just different file format

### SBOM
- Same sidecar generation as MSI, no changes needed

### Reproducible Builds
- Use `ReproducibleBuildOptions` epoch for deterministic content
- MSIX ZIP format supports reproducible output

## COM Interop Definitions

Based on Microsoft's MSIX-Toolkit:

```csharp
[ComImport, Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781")]
internal class AppxFactory { }

[ComImport, Guid("beb94909-e451-438b-b5a7-d79e767b75d8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxFactory
{
    IAppxPackageWriter CreatePackageWriter(
        IStream outputStream,
        ref APPX_PACKAGE_SETTINGS settings);
    // ... other methods
}

[ComImport, Guid("9099e33b-246f-41e4-881a-008eb613f858")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxPackageWriter
{
    void AddPayloadFile(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        [MarshalAs(UnmanagedType.LPWStr)] string contentType,
        APPX_COMPRESSION_OPTION compressionOption,
        IStream inputStream);

    void Close(IStream manifest);
}

internal enum APPX_COMPRESSION_OPTION
{
    NONE = 0, NORMAL = 1, MAXIMUM = 2, FAST = 3, SUPERFAST = 4
}
```

## Content Type Mapping

`AppxPackageWriter` uses MIME types for `AddPayloadFile`:

| Extension | Content Type |
|-----------|-------------|
| `.dll`, `.exe` | `application/x-msdownload` |
| `.xml` | `application/xml` |
| `.json` | `application/json` |
| `.png` | `image/png` |
| `.jpg` | `image/jpeg` |
| `.ico` | `image/x-icon` |
| `.dat` | `application/octet-stream` |
| (default) | `application/octet-stream` |

## Error Handling

All steps return `Result<T>`. Pipeline short-circuits on first failure:

```csharp
public Result<string> Compile(MsixModel model, string outputPath)
{
    var validation = MsixValidator.Validate(model);
    if (!validation.IsValid) return Result<string>.Failure(...);

    var layout = VfsMapper.Resolve(model);
    var manifest = AppxManifestGenerator.Generate(model);
    var registry = model.RegistryEntries.Count > 0
        ? RegistryHiveBuilder.Build(model.RegistryEntries)
        : null;

    var packageResult = AppxPackageWriter.Create(outputPath, manifest, layout, registry);
    if (packageResult.IsFailure) return packageResult;

    var signResult = CodeSigner.Sign(packageResult.Value, model.Signing);
    if (signResult.IsFailure) return signResult;

    if (model.UpdateSettings is not null)
        AppInstallerGenerator.Generate(model, packageResult.Value);

    return Result<string>.Success(packageResult.Value);
}
```

## Dependencies

```xml
<ProjectReference Include="..\FalkForge.Core\FalkForge.Core.csproj" />
```

No external NuGet packages required — COM interop is built into .NET on Windows.

## Testing Strategy

- **Unit tests:** MsixValidator, AppxManifestGenerator, VfsMapper (pure logic, no COM)
- **Integration tests:** MsixCompiler end-to-end (requires Windows, COM APIs)
- **Platform attribute:** `[SupportedOSPlatform("windows")]` on COM-dependent classes
- **Test verification:** Open output .msix as ZIP, validate manifest XML, check file layout
