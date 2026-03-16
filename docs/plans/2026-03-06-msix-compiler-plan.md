# MSIX Compiler Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Add MSIX package output support to FalkForge via a new `FalkForge.Compiler.Msix` project using Windows IAppxFactory COM interop.

**Architecture:** Separate `MsixModel` + `MsixBuilder` (like BundleModel), 7-step compiler pipeline (Validate → VFS → Manifest → Registry.dat → Package → Sign → AppInstaller), COM interop with `IAppxPackageWriter` for package creation.

**Tech Stack:** C# 13, .NET 10, xUnit, COM Interop (IAppxPackaging), P/Invoke (RegLoadKey/RegSaveKeyEx), SignTool.exe

---

## Reference Patterns

- **Separate model:** `src/FalkForge.Compiler.Bundle/BundleModel.cs`
- **Fluent builder:** `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs`
- **Compiler pipeline:** `src/FalkForge.Compiler.Bundle/Compilation/BundleCompiler.cs`
- **Validation:** `src/FalkForge.Core/Validation/ModelValidator.cs` + `ValidationResult.cs`
- **Signing:** `src/FalkForge.Compiler.Msi/Signing/CodeSigner.cs`
- **Result pattern:** `src/FalkForge.Core/Result.cs` — `Result<T>.Success(val)` / `Result<T>.Failure(ErrorKind, msg)`
- **Tests:** `tests/FalkForge.Compiler.Bundle.Tests/RemotePayloadTests.cs` — xUnit `[Fact]`, fluent builder setup

## Key Types (from Core)

```csharp
// Result<T> — src/FalkForge.Core/Result.cs
Result<string>.Success(path)
Result<string>.Failure(ErrorKind.Validation, "message")

// Error — src/FalkForge.Core/Error.cs
readonly record struct Error(ErrorKind Kind, string Message)

// ValidationResult — src/FalkForge.Core/Validation/ValidationResult.cs
validation.IsValid, validation.AddError("CODE", "msg"), validation.AddWarning("CODE", "msg")

// SigningOptions — src/FalkForge.Core/Models/SigningOptions.cs
CertificatePath, CertificateThumbprint, TimestampUrl, DigestAlgorithm

// ProcessorArchitecture — src/FalkForge.Core/ProcessorArchitecture.cs
X86, X64, Arm64

// InstallScope — src/FalkForge.Core/InstallScope.cs
PerMachine, PerUser
```

## Build System

```xml
<!-- Directory.Build.props: net10.0, nullable, TreatWarningsAsErrors=true -->
<!-- Directory.Packages.props: central package management -->
<!-- FalkForge.slnx: add new projects under /src/ and /tests/ folders -->
```

---

### Task 1: Project Scaffolding & MsixModel

**Files:**
- Create: `src/FalkForge.Compiler.Msix/FalkForge.Compiler.Msix.csproj`
- Create: `src/FalkForge.Compiler.Msix/MsixModel.cs`
- Create: `src/FalkForge.Compiler.Msix/MsixApplication.cs`
- Create: `src/FalkForge.Compiler.Msix/MsixVisualElements.cs`
- Create: `src/FalkForge.Compiler.Msix/MsixExtension.cs`
- Create: `src/FalkForge.Compiler.Msix/MsixUpdateSettings.cs`
- Create: `src/FalkForge.Compiler.Msix/MsixPackageDependency.cs`
- Create: `src/FalkForge.Compiler.Msix/MsixRegistryEntry.cs`
- Create: `src/FalkForge.Compiler.Msix/VfsMappingMode.cs`
- Create: `src/FalkForge.Compiler.Msix/VfsOverride.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/FalkForge.Compiler.Msix.Tests.csproj`
- Create: `tests/FalkForge.Compiler.Msix.Tests/MsixModelTests.cs`
- Modify: `FalkForge.slnx` — add both projects

**csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows</TargetFramework>
        <RootNamespace>FalkForge.Compiler.Msix</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
        <ProjectReference Include="..\FalkForge.Core\FalkForge.Core.csproj" />
    </ItemGroup>
</Project>
```

**Test csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows</TargetFramework>
        <IsPackable>false</IsPackable>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" />
        <PackageReference Include="xunit" />
        <PackageReference Include="xunit.runner.visualstudio" />
        <PackageReference Include="coverlet.collector" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="..\..\src\FalkForge.Compiler.Msix\FalkForge.Compiler.Msix.csproj" />
    </ItemGroup>
</Project>
```

**MsixModel** — immutable model following BundleModel pattern:

```csharp
namespace FalkForge.Compiler.Msix;

public sealed class MsixModel
{
    // Identity
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required Version Version { get; init; }
    public ProcessorArchitecture Architecture { get; init; } = ProcessorArchitecture.X64;

    // Properties
    public required string DisplayName { get; init; }
    public required string PublisherDisplayName { get; init; }
    public string? Description { get; init; }
    public string? LogoPath { get; init; }

    // Applications
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

    // Extensions
    public IReadOnlyList<MsixExtension> Extensions { get; init; } = [];

    // VFS
    public VfsMappingMode VfsMapping { get; init; } = VfsMappingMode.Auto;
    public IReadOnlyList<VfsOverride> VfsOverrides { get; init; } = [];

    // Cross-cutting
    public InstallScope Scope { get; init; } = InstallScope.PerMachine;
    public SigningOptions? Signing { get; init; }
    public SbomOptions? SbomOptions { get; init; }

    // Auto-update
    public MsixUpdateSettings? UpdateSettings { get; init; }
}
```

**Tests (5):**
1. `MsixModel_RequiredProperties_CanBeConstructed` — create model with all required fields
2. `MsixModel_DefaultValues_AreCorrect` — verify defaults (Architecture=X64, Scope=PerMachine, MinWindowsVersion)
3. `MsixModel_OptionalCollections_DefaultToEmpty` — Files, RegistryEntries, etc. default to []
4. `MsixModel_WithApplications_StoresCorrectly` — verify Applications list round-trips
5. `MsixModel_WithCapabilities_StoresCorrectly` — verify Capabilities/RestrictedCapabilities

**Verify:** `dotnet build D:/Git/FalkInstaller/FalkForge.slnx` — 0 errors. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 5 pass.

---

### Task 2: MsixBuilder Fluent API

**Files:**
- Create: `src/FalkForge.Compiler.Msix/Builders/MsixBuilder.cs`
- Create: `src/FalkForge.Compiler.Msix/Builders/MsixApplicationBuilder.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/Builders/MsixBuilderTests.cs`

**MsixBuilder** — fluent API that produces MsixModel, following BundleBuilder pattern:

```csharp
public sealed class MsixBuilder
{
    private string _name = string.Empty;
    private string _publisher = string.Empty;
    private string _displayName = string.Empty;
    private string _publisherDisplayName = string.Empty;
    private Version _version = new(1, 0, 0, 0);
    private ProcessorArchitecture _architecture = ProcessorArchitecture.X64;
    private InstallScope _scope = InstallScope.PerMachine;
    private string _minWindowsVersion = "10.0.17763.0";
    // ... all private fields

    public MsixBuilder Name(string name) { _name = name; return this; }
    public MsixBuilder Publisher(string publisher) { _publisher = publisher; return this; }
    public MsixBuilder DisplayName(string displayName) { _displayName = displayName; return this; }
    // ... all fluent methods

    public MsixBuilder Application(string id, string executable, Action<MsixApplicationBuilder> configure)
    {
        var builder = new MsixApplicationBuilder(id, executable);
        configure(builder);
        _applications.Add(builder.Build());
        return this;
    }

    public MsixBuilder Files(Action<FileSetBuilder> configure)
    {
        var builder = new FileSetBuilder();
        configure(builder);
        _files.AddRange(builder.Build());
        return this;
    }

    public MsixBuilder Signing(Action<SigningOptionsBuilder> configure) { ... }
    public MsixBuilder Capability(string capability) { ... }
    public MsixBuilder RestrictedCapability(string capability) { ... }

    public MsixModel Build() => new()
    {
        Name = _name,
        Publisher = _publisher,
        // ... map all fields
    };
}
```

**Tests (8):**
1. `Build_MinimalModel_SetsRequiredFields` — Name, Publisher, DisplayName, Application
2. `Build_SetsArchitecture` — verify non-default architecture
3. `Build_AddFiles_IncludedInModel` — Files() adds to model
4. `Build_AddCapabilities_IncludedInModel`
5. `Build_AddRestrictedCapabilities_IncludedInModel`
6. `Build_MultipleApplications_AllIncluded`
7. `Build_SigningOptions_SetCorrectly`
8. `Build_UpdateSettings_SetCorrectly`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 13 pass.

---

### Task 3: MsixValidator

**Files:**
- Create: `src/FalkForge.Compiler.Msix/MsixValidator.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/MsixValidatorTests.cs`

**MsixValidator** — static class following ModelValidator pattern:

```csharp
public static class MsixValidator
{
    public static ValidationResult Validate(MsixModel model)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(model.Name))
            result.AddError("MSIX001", "Package Name is required.");

        if (string.IsNullOrWhiteSpace(model.Publisher))
            result.AddError("MSIX002", "Publisher is required.");
        else if (!model.Publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            result.AddError("MSIX003", "Publisher must start with 'CN=' (certificate subject format).");

        if (model.Version.Revision < 0)
            result.AddError("MSIX004", "Version must have 4 parts (Major.Minor.Build.Revision).");

        if (model.Applications.Count == 0)
            result.AddError("MSIX005", "At least one Application is required.");

        if (string.IsNullOrWhiteSpace(model.DisplayName))
            result.AddError("MSIX006", "DisplayName is required.");

        if (string.IsNullOrWhiteSpace(model.PublisherDisplayName))
            result.AddError("MSIX007", "PublisherDisplayName is required.");

        if (model.Signing is null)
            result.AddError("MSIX008", "MSIX packages must be signed. Provide SigningOptions.");

        // MSIX limitations — reject unsupported features
        // (custom actions, drivers, system env vars are not modeled in MsixModel so no check needed)

        foreach (var app in model.Applications)
        {
            if (string.IsNullOrWhiteSpace(app.Id))
                result.AddError("MSIX010", "Application Id is required.");
            if (string.IsNullOrWhiteSpace(app.Executable))
                result.AddError("MSIX011", "Application Executable is required.");
        }

        if (!System.Version.TryParse(model.MinWindowsVersion, out _))
            result.AddError("MSIX012", $"Invalid MinWindowsVersion: {model.MinWindowsVersion}");

        return result;
    }
}
```

Note: `ValidationResult.AddError` and `AddWarning` are `internal` in FalkForge.Core. The new project will need `InternalsVisibleTo` in FalkForge.Core.csproj, OR we change the validator to return `Result<Unit>` instead. Check what BundleValidator does — `BundleValidator.Validate()` returns `Result<Unit>`, not `ValidationResult`. **Follow the BundleValidator pattern** — return `Result<Unit>`.

**Tests (10):**
1. `Validate_ValidModel_ReturnsSuccess`
2. `Validate_EmptyName_ReturnsFailure`
3. `Validate_EmptyPublisher_ReturnsFailure`
4. `Validate_PublisherWithoutCN_ReturnsFailure`
5. `Validate_NoApplications_ReturnsFailure`
6. `Validate_EmptyDisplayName_ReturnsFailure`
7. `Validate_NoSigning_ReturnsFailure`
8. `Validate_EmptyApplicationId_ReturnsFailure`
9. `Validate_EmptyApplicationExecutable_ReturnsFailure`
10. `Validate_InvalidMinWindowsVersion_ReturnsFailure`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 23 pass.

---

### Task 4: VfsMapper

**Files:**
- Create: `src/FalkForge.Compiler.Msix/Packaging/VfsMapper.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/Packaging/VfsMapperTests.cs`

**VfsMapper** — resolves file install paths to MSIX VFS folder structure:

```csharp
public static class VfsMapper
{
    private static readonly Dictionary<string, string> KnownFolderToVfs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProgramFilesFolder"] = "VFS/ProgramFilesX64",
        ["ProgramFiles64Folder"] = "VFS/ProgramFilesX64",
        ["ProgramFilesX86Folder"] = "VFS/ProgramFilesX86",
        ["CommonFilesFolder"] = "VFS/ProgramFilesCommonX64",
        ["CommonFiles64Folder"] = "VFS/ProgramFilesCommonX64",
        ["SystemFolder"] = "VFS/SystemX64",
        ["System64Folder"] = "VFS/SystemX64",
        ["WindowsFolder"] = "VFS/Windows",
        ["CommonAppDataFolder"] = "VFS/CommonAppData",
        ["AppDataFolder"] = "VFS/AppData",
        ["LocalAppDataFolder"] = "VFS/LocalAppData",
        ["FontsFolder"] = "VFS/Fonts",
    };

    public static IReadOnlyList<VfsFileEntry> Resolve(MsixModel model)
    {
        return model.VfsMapping switch
        {
            VfsMappingMode.Auto => ResolveAuto(model),
            VfsMappingMode.Manual => ResolveManual(model),
            _ => ResolveAuto(model)
        };
    }
    // ...
}

public sealed class VfsFileEntry
{
    public required string SourcePath { get; init; }
    public required string PackageRelativePath { get; init; }
}
```

**Tests (8):**
1. `Resolve_AutoMode_ProgramFilesFolder_MapsToVfs`
2. `Resolve_AutoMode_SystemFolder_MapsToVfs`
3. `Resolve_AutoMode_CommonAppData_MapsToVfs`
4. `Resolve_AutoMode_UnknownFolder_MapsToRoot`
5. `Resolve_AutoMode_X86Package_UsesProgramFilesX86`
6. `Resolve_ManualMode_UsesOverrides`
7. `Resolve_ManualMode_NoOverride_MapsToRoot`
8. `Resolve_EmptyFiles_ReturnsEmpty`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 31 pass.

---

### Task 5: AppxManifestGenerator

**Files:**
- Create: `src/FalkForge.Compiler.Msix/Manifest/AppxManifestGenerator.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/Manifest/AppxManifestGeneratorTests.cs`

**AppxManifestGenerator** — transforms MsixModel into AppxManifest.xml:

```csharp
public static class AppxManifestGenerator
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

    public static Result<XDocument> Generate(MsixModel model)
    {
        var doc = new XDocument(
            new XElement(Ns + "Package",
                new XAttribute(XNamespace.Xmlns + "uap", Uap),
                new XAttribute(XNamespace.Xmlns + "uap10", Uap10),
                new XAttribute(XNamespace.Xmlns + "rescap", Rescap),
                new XAttribute(XNamespace.Xmlns + "desktop", Desktop),
                new XAttribute("IgnorableNamespaces", "uap uap10 rescap desktop"),
                GenerateIdentity(model),
                GenerateProperties(model),
                GenerateDependencies(model),
                GenerateCapabilities(model),
                GenerateApplications(model)
            )
        );
        return Result<XDocument>.Success(doc);
    }
    // private helper methods...
}
```

**Tests (10):**
1. `Generate_MinimalModel_ProducesValidXml`
2. `Generate_Identity_IncludesNamePublisherVersionArch`
3. `Generate_Properties_IncludesDisplayNameAndLogo`
4. `Generate_Dependencies_IncludesTargetDeviceFamily`
5. `Generate_Capabilities_IncludesGeneralCapabilities`
6. `Generate_RestrictedCapabilities_IncludesRescap`
7. `Generate_SingleApplication_IncludesAppElement`
8. `Generate_MultipleApplications_IncludesAll`
9. `Generate_VisualElements_IncludesDisplayNameAndColor`
10. `Generate_PackageDependencies_IncludesDependencyElements`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 41 pass.

---

### Task 6: AppInstallerGenerator

**Files:**
- Create: `src/FalkForge.Compiler.Msix/Manifest/AppInstallerGenerator.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/Manifest/AppInstallerGeneratorTests.cs`

**AppInstallerGenerator** — produces `.appinstaller` XML for auto-updates:

```csharp
public static class AppInstallerGenerator
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/appinstaller/2021";

    public static Result<XDocument> Generate(MsixModel model, string msixFileName)
    {
        if (model.UpdateSettings is null)
            return Result<XDocument>.Failure(ErrorKind.InvalidConfiguration, "UpdateSettings is required");

        var settings = model.UpdateSettings;
        var doc = new XDocument(
            new XElement(Ns + "AppInstaller",
                new XAttribute("Version", model.Version.ToString()),
                new XAttribute("Uri", settings.AppInstallerUri),
                new XElement(Ns + "MainPackage",
                    new XAttribute("Name", model.Name),
                    new XAttribute("Publisher", model.Publisher),
                    new XAttribute("Version", model.Version.ToString()),
                    new XAttribute("ProcessorArchitecture", MapArchitecture(model.Architecture)),
                    new XAttribute("Uri", GetPackageUri(settings.AppInstallerUri, msixFileName))),
                GenerateUpdateSettings(settings)
            )
        );
        return Result<XDocument>.Success(doc);
    }
}
```

**Tests (6):**
1. `Generate_WithUpdateSettings_ProducesValidXml`
2. `Generate_NoUpdateSettings_ReturnsFailure`
3. `Generate_IncludesMainPackageAttributes`
4. `Generate_IncludesOnLaunchSettings`
5. `Generate_AutomaticBackgroundTask_Included`
6. `Generate_ForceUpdateFromAnyVersion_Included`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 47 pass.

---

### Task 7: COM Interop Definitions

**Files:**
- Create: `src/FalkForge.Compiler.Msix/Interop/AppxInterop.cs` — all COM interfaces in one file
- Create: `tests/FalkForge.Compiler.Msix.Tests/Interop/AppxInteropTests.cs`

**COM Interop** — based on Microsoft MSIX-Toolkit definitions:

```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FalkForge.Compiler.Msix.Interop;

[ComImport, Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781")]
internal class AppxFactory { }

[ComImport, Guid("beb94909-e451-438b-b5a7-d79e767b75d8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxFactory
{
    void _VtblGap_CreatePackageReader();  // skip IAppxPackageReader
    IAppxPackageWriter CreatePackageWriter(
        IStream outputStream,
        [In] ref APPX_PACKAGE_SETTINGS settings);
    // remaining vtable gaps...
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

[StructLayout(LayoutKind.Sequential)]
internal struct APPX_PACKAGE_SETTINGS
{
    [MarshalAs(UnmanagedType.Bool)] public bool ForceZip32;
    public IntPtr HashMethod;  // IUri*
}

internal enum APPX_COMPRESSION_OPTION
{
    None = 0, Normal = 1, Maximum = 2, Fast = 3, SuperFast = 4
}
```

Also add `IAppxBundleFactory` and `IAppxBundleWriter` for MSIX Bundles.

**Tests (2):** Smoke tests only — COM instantiation requires Windows
1. `AppxFactory_CanBeCreated_OnWindows` — `[SupportedOSPlatform("windows")]`, verify `new AppxFactory()` doesn't throw
2. `AppxCompressionOption_ValuesMatchWindowsApi` — verify enum values match expected integers

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 49 pass.

---

### Task 8: AppxPackageWriter (COM Wrapper)

**Files:**
- Create: `src/FalkForge.Compiler.Msix/Packaging/AppxPackageWriter.cs`
- Create: `src/FalkForge.Compiler.Msix/Packaging/ContentTypeMapper.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/Packaging/ContentTypeMapperTests.cs`

**AppxPackageWriter** — managed wrapper around COM IAppxPackageWriter:

```csharp
[SupportedOSPlatform("windows")]
internal sealed class AppxPackageWriter : IDisposable
{
    public static Result<string> CreatePackage(
        string outputPath,
        XDocument manifest,
        IReadOnlyList<VfsFileEntry> files,
        byte[]? registryHive)
    {
        // 1. Create output stream via SHCreateStreamOnFileEx
        // 2. CoCreateInstance IAppxFactory
        // 3. factory.CreatePackageWriter(stream, settings)
        // 4. For each file: writer.AddPayloadFile(relativePath, contentType, compression, fileStream)
        // 5. If registryHive: writer.AddPayloadFile("Registry.dat", "application/octet-stream", ...)
        // 6. writer.Close(manifestStream)
        // 7. Return output path
    }
}
```

**ContentTypeMapper** — maps file extensions to MIME types:

```csharp
public static class ContentTypeMapper
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "application/x-msdownload",
        [".dll"] = "application/x-msdownload",
        [".xml"] = "application/xml",
        [".json"] = "application/json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".ico"] = "image/x-icon",
        [".txt"] = "text/plain",
        [".config"] = "application/xml",
        [".dat"] = "application/octet-stream",
    };

    public static string GetContentType(string fileName)
        => Map.TryGetValue(Path.GetExtension(fileName), out var ct) ? ct : "application/octet-stream";
}
```

**Tests (8):**
1. `GetContentType_Exe_ReturnsCorrectType`
2. `GetContentType_Dll_ReturnsCorrectType`
3. `GetContentType_Png_ReturnsImagePng`
4. `GetContentType_Json_ReturnsApplicationJson`
5. `GetContentType_UnknownExtension_ReturnsOctetStream`
6. `GetContentType_CaseInsensitive_Works`
7. `GetContentType_Config_ReturnsXml`
8. `GetContentType_NoExtension_ReturnsOctetStream`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 57 pass.

---

### Task 9: RegistryHiveBuilder

**Files:**
- Create: `src/FalkForge.Compiler.Msix/Registry/RegistryHiveBuilder.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/Registry/RegistryHiveBuilderTests.cs`

**RegistryHiveBuilder** — creates standard Windows registry hive via P/Invoke:

```csharp
[SupportedOSPlatform("windows")]
internal static class RegistryHiveBuilder
{
    public static Result<byte[]> Build(IReadOnlyList<MsixRegistryEntry> entries)
    {
        if (entries.Count == 0)
            return Result<byte[]>.Failure(ErrorKind.InvalidConfiguration, "No registry entries");

        // 1. Create temp hive file path
        // 2. Create empty hive via ORCreateHive / RegCreateKeyEx + RegSaveKeyEx
        //    Alternative: use Microsoft.Win32.RegistryKey.OpenBaseKey + LoadSubKey pattern
        // 3. For each entry: create key, set value with appropriate type
        // 4. Save hive to file
        // 5. Read file bytes and return
        // 6. Cleanup temp files
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegLoadKey(IntPtr hKey, string subKey, string file);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegSaveKeyEx(IntPtr hKey, string file, IntPtr securityAttributes, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegUnLoadKey(IntPtr hKey, string subKey);
}
```

**Tests (5):** Integration tests requiring Windows
1. `Build_SingleStringEntry_CreatesHive` — verify non-empty byte array returned
2. `Build_DWordEntry_CreatesHive`
3. `Build_MultipleEntries_CreatesHive`
4. `Build_EmptyEntries_ReturnsFailure`
5. `Build_HklmAndHkcuEntries_BothInHive`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 62 pass.

---

### Task 10: MsixCompiler (Pipeline)

**Files:**
- Create: `src/FalkForge.Compiler.Msix/MsixCompiler.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/MsixCompilerTests.cs`

**MsixCompiler** — 7-step pipeline:

```csharp
[SupportedOSPlatform("windows")]
public sealed class MsixCompiler
{
    public Result<string> Compile(MsixModel model, string outputPath)
    {
        // Step 1: Validate
        var validation = MsixValidator.Validate(model);
        if (validation.IsFailure) return Result<string>.Failure(validation.Error);

        // Step 2: Resolve VFS
        var layout = VfsMapper.Resolve(model);

        // Step 3: Generate manifest
        var manifestResult = AppxManifestGenerator.Generate(model);
        if (manifestResult.IsFailure) return Result<string>.Failure(manifestResult.Error);

        // Step 4: Build registry hive (optional)
        byte[]? registryHive = null;
        if (model.RegistryEntries.Count > 0)
        {
            var hiveResult = RegistryHiveBuilder.Build(model.RegistryEntries);
            if (hiveResult.IsFailure) return Result<string>.Failure(hiveResult.Error);
            registryHive = hiveResult.Value;
        }

        // Step 5: Create MSIX package
        var msixFileName = $"{FileNameSanitizer.Sanitize(model.DisplayName)}-{model.Version}.msix";
        var msixPath = Path.Combine(outputPath, msixFileName);
        var packageResult = AppxPackageWriter.CreatePackage(msixPath, manifestResult.Value, layout, registryHive);
        if (packageResult.IsFailure) return Result<string>.Failure(packageResult.Error);

        // Step 6: Sign
        var signer = new CodeSigner();
        var signResult = signer.Sign(msixPath, model.Signing!);
        if (signResult.IsFailure) return Result<string>.Failure(signResult.Error);

        // Step 7: Generate .appinstaller (optional)
        if (model.UpdateSettings is not null)
        {
            var appInstallerResult = AppInstallerGenerator.Generate(model, msixFileName);
            if (appInstallerResult.IsSuccess)
            {
                var appInstallerPath = Path.ChangeExtension(msixPath, ".appinstaller");
                appInstallerResult.Value.Save(appInstallerPath);
            }
        }

        return Result<string>.Success(msixPath);
    }
}
```

**Tests (6):** Integration tests — require Windows, signing cert, and temp files
1. `Compile_ValidModel_CreatesMsixFile` — verify file exists and is non-empty
2. `Compile_InvalidModel_ReturnsFailure` — empty name
3. `Compile_WithRegistryEntries_IncludesRegistryDat`
4. `Compile_WithUpdateSettings_CreatesAppInstaller`
5. `Compile_OutputDirectoryCreated_IfNotExists`
6. `Compile_NoSigning_ReturnsFailure` — MSIX must be signed

Note: Full integration tests require a self-signed cert. Create a test helper that generates one, or mock the signer for unit tests.

**Verify:** `dotnet build D:/Git/FalkInstaller/FalkForge.slnx` — 0 errors. `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 68 pass.

---

### Task 11: MsixBundleModel + MsixBundleCompiler

**Files:**
- Create: `src/FalkForge.Compiler.Msix/MsixBundleModel.cs`
- Create: `src/FalkForge.Compiler.Msix/MsixBundleCompiler.cs`
- Create: `src/FalkForge.Compiler.Msix/Builders/MsixBundleBuilder.cs`
- Create: `tests/FalkForge.Compiler.Msix.Tests/MsixBundleBuilderTests.cs`

**MsixBundleBuilder** — fluent API for multi-arch bundles:

```csharp
public sealed class MsixBundleBuilder
{
    public MsixBundleBuilder Name(string name) { ... }
    public MsixBundleBuilder Publisher(string publisher) { ... }
    public MsixBundleBuilder Version(Version version) { ... }
    public MsixBundleBuilder Package(string filePath, ProcessorArchitecture arch) { ... }
    public MsixBundleBuilder Signing(Action<SigningOptionsBuilder> configure) { ... }
    public MsixBundleModel Build() => new() { ... };
}
```

**MsixBundleCompiler** — uses `IAppxBundleFactory` COM:

```csharp
[SupportedOSPlatform("windows")]
public sealed class MsixBundleCompiler
{
    public Result<string> Compile(MsixBundleModel model, string outputPath)
    {
        // 1. Validate (at least 1 package, all files exist)
        // 2. Create IAppxBundleFactory → IAppxBundleWriter
        // 3. AddPayloadPackage for each .msix
        // 4. Close
        // 5. Sign
        // 6. Return path
    }
}
```

**Tests (5):**
1. `MsixBundleBuilder_Build_SetsProperties`
2. `MsixBundleBuilder_MultiplePackages_AllIncluded`
3. `MsixBundleBuilder_SigningOptions_Set`
4. `MsixBundleCompiler_NoPackages_ReturnsFailure` (validation)
5. `MsixBundleCompiler_MissingFile_ReturnsFailure`

**Verify:** `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Msix.Tests/` — 73 pass.

---

### Task 12: Full Verification

1. `dotnet build D:/Git/FalkInstaller/FalkForge.slnx` — 0 errors, 0 warnings
2. `dotnet test D:/Git/FalkInstaller/FalkForge.slnx` — all tests pass
3. Verify solution file includes both new projects
4. Verify COM interop smoke tests pass on Windows

---

## Final Project Structure

```
src/FalkForge.Compiler.Msix/
├── FalkForge.Compiler.Msix.csproj
├── MsixModel.cs
├── MsixApplication.cs
├── MsixVisualElements.cs
├── MsixExtension.cs  (+ subtypes: FileTypeAssociation, Protocol, ComServer, Service, StartupTask, FirewallRule)
├── MsixUpdateSettings.cs
├── MsixPackageDependency.cs
├── MsixRegistryEntry.cs
├── VfsMappingMode.cs
├── VfsOverride.cs
├── MsixBundleModel.cs
├── MsixValidator.cs
├── MsixCompiler.cs
├── MsixBundleCompiler.cs
├── Builders/
│   ├── MsixBuilder.cs
│   ├── MsixApplicationBuilder.cs
│   └── MsixBundleBuilder.cs
├── Manifest/
│   ├── AppxManifestGenerator.cs
│   └── AppInstallerGenerator.cs
├── Packaging/
│   ├── AppxPackageWriter.cs
│   ├── ContentTypeMapper.cs
│   └── VfsMapper.cs
├── Registry/
│   └── RegistryHiveBuilder.cs
└── Interop/
    └── AppxInterop.cs

tests/FalkForge.Compiler.Msix.Tests/
├── FalkForge.Compiler.Msix.Tests.csproj
├── MsixModelTests.cs
├── MsixValidatorTests.cs
├── MsixCompilerTests.cs
├── MsixBundleBuilderTests.cs
├── Builders/
│   └── MsixBuilderTests.cs
├── Manifest/
│   ├── AppxManifestGeneratorTests.cs
│   └── AppInstallerGeneratorTests.cs
├── Packaging/
│   ├── ContentTypeMapperTests.cs
│   └── VfsMapperTests.cs
├── Registry/
│   └── RegistryHiveBuilderTests.cs
└── Interop/
    └── AppxInteropTests.cs
```
