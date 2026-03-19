# FalkForge MAS Gap Analysis: WiX Parity Assessment

## Overview

Analysis of gaps discovered while replicating the WiX MultiAccessStyra.Setup in FalkForge. The MAS suite consists of four MSI packages (MultiAccess, MultiServer, MultiServerEx, Concatenate, Konfigurera) and one EXE bundle. This document catalogs every feature gap that prevents true 1:1 behavioral parity with the original WiX installer.

Source files analyzed:
- `demo/MAS/packages/MultiAccess/Program.cs`
- `demo/MAS/packages/MultiServer/Program.cs`
- `demo/MAS/packages/MultiServerEx/Program.cs`
- `demo/MAS/bundle/Program.cs`

---

## Critical Gaps (Blocking 1:1 Parity)

### 1. ServiceModel.Arguments — Service Command-Line Arguments

**What WiX does**: `<ServiceInstall Arguments="DSN=[ODBCNAME]" .../>` passes runtime arguments to the service executable. The MSI `ServiceInstall` table's `Arguments` column stores this value.

**What FalkForge has**: `ServiceModel` has `Name`, `DisplayName`, `Executable`, `Description`, `StartMode`, `Account`, `UserName`, `Password`, `Dependencies`, `FailureActions`. No `Arguments` property.

**What's missing**: `ServiceModel.Arguments` (string?) and `ServiceBuilder.Arguments(string)` fluent method.

**Suggested fix**:
1. Add `public string? Arguments { get; init; }` to `ServiceModel`.
2. Add `public string? Arguments { get; set; }` to `ServiceBuilder` and wire through `Build()`.
3. Emit the value in `TableEmitter` when writing the `ServiceInstall` MSI table row.
4. Add validation rule `SVC009`: if `Arguments` contains unresolved property syntax, warn.

**Affected projects**: `FalkForge.Core` (Models/ServiceModel.cs, Builders/ServiceBuilder.cs), `FalkForge.Compiler.Msi` (Tables/TableEmitter.cs)

**Complexity**: Low (1-2 hours). Single property addition, one table column.

---

### 2. Service Account as Property Reference

**What WiX does**: `<ServiceInstall Account="[SERVICEACCOUNT]" .../>` accepts an MSI property reference for the service account. At install time, the value of `SERVICEACCOUNT` determines whether the service runs as LocalSystem, NetworkService, or a domain user.

**What FalkForge has**: `ServiceAccount` is a fixed enum (`LocalSystem`, `LocalService`, `NetworkService`, `User`). The `Account` property on `ServiceBuilder` takes this enum. To use a property reference, you must set `Account = ServiceAccount.User` and pass `UserName = "[SERVICEACCOUNT]"`, which works but forces the MSI to always write a `User` account type row rather than letting the property resolve to a well-known account.

**What's missing**: The ability for `Account` to accept an MSI property string that resolves at install time. The current enum-only approach means the ServiceInstall table always emits a fixed account type.

**Suggested fix**:
1. Add `public string? AccountProperty { get; init; }` to `ServiceModel` — when set, overrides the `Account` enum.
2. Add `ServiceBuilder.AccountProperty(string propertyRef)` fluent method.
3. In `TableEmitter`, if `AccountProperty` is set, emit it raw into the ServiceInstall `StartName` column instead of deriving from the enum.

**Affected projects**: `FalkForge.Core` (Models/ServiceModel.cs, Builders/ServiceBuilder.cs), `FalkForge.Compiler.Msi` (Tables/TableEmitter.cs)

**Complexity**: Low (1-2 hours).

---

### 3. Service Component Condition

**What WiX does**: A `<Component>` containing `<ServiceInstall>` can have a `<Condition>` child element. Example: `<Condition>ASSERVICE ~= "true"</Condition>` means the service is only installed when the condition evaluates to true.

**What FalkForge has**: `FileEntryModel` has `ComponentCondition` (set via `FileSetBuilder.ComponentCondition()`). `ServiceModel` has no equivalent. Services are always installed unconditionally.

**What's missing**: `ServiceModel.ComponentCondition` (string?) and `ServiceBuilder.Condition(string)` / `ServiceBuilder.Condition(Condition)` fluent methods.

**Suggested fix**:
1. Add `public string? ComponentCondition { get; init; }` to `ServiceModel`.
2. Add fluent methods on `ServiceBuilder`.
3. In `ComponentResolver` / `TableEmitter`, emit the condition in the `Condition` table for the service's component.
4. Add validation rule `SVC010`: condition string must not be empty if set.

**Affected projects**: `FalkForge.Core` (Models/ServiceModel.cs, Builders/ServiceBuilder.cs), `FalkForge.Compiler.Msi` (ComponentResolver.cs, Tables/TableEmitter.cs)

**Complexity**: Medium (2-4 hours). Requires wiring through component resolution.

---

### 4. FileEntry NeverOverwrite and Permanent

**What WiX does**: `<File NeverOverwrite="yes" Permanent="yes" .../>` on config files. `NeverOverwrite` prevents overwriting an existing file on upgrade. `Permanent` prevents deletion on uninstall. These map to MSI `Component` table attributes (bits `msidbComponentAttributesNeverOverwrite` = 0x100 and `msidbComponentAttributesPermanent` = 0x10).

**What FalkForge has**: `FileEntryModel` has `SourcePath`, `TargetDirectory`, `FileName`, `IsKeyPath`, `ComponentId`, `ComponentGuid`, `FeatureRef`, `Vital`, `ComponentCondition`. No `NeverOverwrite` or `Permanent` properties.

**What's missing**: Both `NeverOverwrite` and `Permanent` bool properties on `FileEntryModel` and corresponding builder methods.

**Suggested fix**:
1. Add `public bool NeverOverwrite { get; init; }` and `public bool Permanent { get; init; }` to `FileEntryModel`.
2. Add `FileSetBuilder.NeverOverwrite()` and `FileSetBuilder.Permanent()` fluent methods.
3. In `ComponentResolver`, set the corresponding attribute bits on the component when these flags are true.
4. Add validation: `NeverOverwrite` and `Permanent` are only meaningful when the file has its own component (not merged with others).

**Affected projects**: `FalkForge.Core` (Models/FileEntryModel.cs, Builders/FileSetBuilder.cs), `FalkForge.Compiler.Msi` (ComponentResolver.cs, Tables/TableEmitter.cs)

**Complexity**: Medium (2-4 hours). Attribute bit manipulation in component table emission.

---

### 5. Service Permissions (util:PermissionEx on ServiceInstall)

**What WiX does**: `<util:PermissionEx>` on `<ServiceInstall>` sets fine-grained service access rights (GenericAll, ServiceChangeConfig, ServiceStart, ServiceStop, etc.) for specified users/groups. This writes to the `MsiLockPermissionsEx` table with `LockObject` referencing the service name and `Table = "ServiceInstall"`.

**What FalkForge has**: `PermissionModel` targets `File`, `Registry`, and `CreateFolder` tables. The `PermissionBuilder.ForTable()` method accepts a string, but the `TableEmitter` and validation only handle those three table types. There is no fluent API path from `ServiceBuilder` to `PermissionBuilder`.

**What's missing**:
- Support for `Table = "ServiceInstall"` in `PermissionModel` / `TableEmitter`.
- A `ServiceBuilder.Permission(Action<PermissionBuilder>)` fluent method.
- Named permission constants (e.g., `ServiceRights.GenericAll`, `ServiceRights.Start`, `ServiceRights.Stop`) instead of raw SDDL or int values.

**Suggested fix**:
1. Extend `TableEmitter` to emit `MsiLockPermissionsEx` rows for `ServiceInstall` table.
2. Add `ServiceBuilder.Permission(Action<PermissionBuilder> configure)` that sets `ForTable("ServiceInstall")` automatically.
3. Optionally add a `ServicePermission` helper with named constants for common service access rights.

**Affected projects**: `FalkForge.Core` (Builders/ServiceBuilder.cs), `FalkForge.Compiler.Msi` (Tables/TableEmitter.cs), optionally `FalkForge.Extensions.Util`

**Complexity**: Medium (3-5 hours).

---

### 6. Prerequisite PackageGroups (NetFx, VCRedist, ODBC, SQL)

**What WiX does**: `<PackageGroupRef Id="NetFx472Redist"/>`, `<PackageGroupRef Id="VCRedist"/>`, etc. These reference pre-built package group definitions (from WiX extensions like `WixToolset.Netfx.wixext`) that handle detection, download, and install of common prerequisites.

**What FalkForge has**: Individual `MsiPackage()` and `ExePackage()` in the chain. No concept of reusable, pre-defined package groups for common prerequisites. Users must manually define each prerequisite as an EXE/MSI package with detection conditions.

**What's missing**:
- A `PackageGroup` abstraction (reusable named set of packages).
- Built-in package group definitions for common prerequisites (.NET Framework, VC++ Redistributable, ODBC drivers, SQL Express).
- `ChainBuilder.PackageGroupRef(string id)` to reference them.

**Suggested fix**:
1. Add `PackageGroupModel` and `PackageGroupBuilder` to `FalkForge.Compiler.Bundle`.
2. Create `FalkForge.Extensions.Prerequisites` (or extend `FalkForge.Extensions.DotNet`) with built-in groups:
   - `NetFx472Redist` (detect via registry `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release >= 461808`)
   - `VCRedist14x64` (detect via registry, download URL, silent args)
   - `OdbcDriver17` (detect via registry `HKLM\SOFTWARE\ODBC\ODBCINST.INI\ODBC Driver 17 for SQL Server`)
   - `SqlExpress2017` (detect via registry, silent args)
3. Add `ChainBuilder.PackageGroup(string id)` and `ChainBuilder.PackageGroup(Action<PackageGroupBuilder>)`.

**Affected projects**: `FalkForge.Compiler.Bundle` (Builders/), `FalkForge.Extensions.DotNet` or new `FalkForge.Extensions.Prerequisites`

**Complexity**: High (8-16 hours). New abstraction layer plus built-in definitions with correct detection logic.

---

### 7. Bundle RegistrySearch for Detection Variables

**What WiX does**: `<util:RegistrySearch Variable="VCIsInstalled" Root="HKLM" Key="..." Value="..." Result="exists"/>` sets a bundle variable based on a registry key lookup. Used for prerequisite detection (e.g., is VC++ Redist installed?).

**What FalkForge has**: `SearchConditionType` enum includes `RegistryValue`, and `SearchCondition` model has `Type`, `Path`, `Value`, `Comparison`. However, `SearchConditionBuilder` only exposes `FileExists()`, `FileVersion()`, and `DirectoryExists()` methods. There is no `RegistrySearch()` or `RegistryExists()` method.

**What's missing**: `SearchConditionBuilder.RegistrySearch(RegistryRoot root, string key, string? valueName)` and related overloads. The model and engine evaluator may already support it (the enum value exists), but the builder API is incomplete.

**Suggested fix**:
1. Add to `SearchConditionBuilder`:
   ```csharp
   public SearchConditionBuilder RegistryExists(RegistryRoot root, string key, string? valueName = null)
   public SearchConditionBuilder RegistryValue(RegistryRoot root, string key, string valueName, string comparison, string value)
   ```
2. Verify `SearchConditionEvaluator` in the Engine handles `SearchConditionType.RegistryValue`.
3. The `Path` field would encode as `HKLM\key\valueName` (or use a structured format).

**Affected projects**: `FalkForge.Compiler.Bundle` (Builders/SearchConditionBuilder.cs), `FalkForge.Engine` (Detection/SearchConditionEvaluator.cs)

**Complexity**: Low-Medium (2-4 hours). The enum and model exist; just the builder API and evaluator wiring are needed.

---

### 8. Bundle Permanent Package

**What WiX does**: `<ExePackage Permanent="yes" .../>` means the package is never uninstalled when the bundle is removed. Used for setup utilities (DatabaseSetup, OdbcSetup) that run once and don't need uninstall.

**What FalkForge has**: `BundlePackageModel` has no `Permanent` property. `BundlePackageBuilder` has no `Permanent()` method.

**What's missing**: `BundlePackageModel.Permanent` (bool) and `BundlePackageBuilder.Permanent(bool)`.

**Suggested fix**:
1. Add `public bool Permanent { get; init; }` to `BundlePackageModel`.
2. Add `public BundlePackageBuilder Permanent(bool permanent = true)` to `BundlePackageBuilder`.
3. In `ManifestGenerator`, emit the flag to the manifest.
4. In `Engine/Planning/Planner`, skip uninstall actions for permanent packages.
5. In `Engine/Execution/PackageExecutor`, respect the flag during uninstall phase.

**Affected projects**: `FalkForge.Compiler.Bundle` (BundlePackageModel.cs, Builders/BundlePackageBuilder.cs, Compilation/ManifestGenerator.cs), `FalkForge.Engine.Protocol` (Manifest/PackageInfo.cs), `FalkForge.Engine` (Planning/Planner.cs, Execution/PackageExecutor.cs)

**Complexity**: Medium (4-6 hours). Touches compiler, manifest, and engine.

---

### 9. Bundle EnableFeatureSelection for MSI Packages

**What WiX does**: `<MsiPackage EnableFeatureSelection="yes" .../>` allows the bundle UI to present MSI feature selection to the user. The bundle passes feature states to the MSI during install.

**What FalkForge has**: The engine already has `SetFeatureSelectionMessage` (0x020A), `FeatureDetector`, and `FeaturePersistence`. The protocol-level support exists. However, `BundlePackageModel` and `BundlePackageBuilder` have no `EnableFeatureSelection` property to opt-in per package.

**What's missing**: The compiler-side flag on `BundlePackageModel` and `BundlePackageBuilder`, plus manifest emission so the engine knows which packages support feature selection.

**Suggested fix**:
1. Add `public bool EnableFeatureSelection { get; init; }` to `BundlePackageModel`.
2. Add `BundlePackageBuilder.EnableFeatureSelection(bool enable = true)`.
3. Emit in `ManifestGenerator` to `PackageInfo`.
4. The engine already supports the protocol messages; this just enables the opt-in.

**Affected projects**: `FalkForge.Compiler.Bundle` (BundlePackageModel.cs, Builders/BundlePackageBuilder.cs, Compilation/ManifestGenerator.cs), `FalkForge.Engine.Protocol` (Manifest/PackageInfo.cs)

**Complexity**: Low (1-2 hours). Engine support already exists.

---

### 10. Post-Install EXE Execution with Arguments

**What WiX does**: `<ExePackage>` with `InstallArguments` containing formatted property references like `[INSTALLFOLDERMA]`, `[DB_SERVER]`, etc. The bundle engine resolves variables and passes them as command-line arguments to the EXE.

**What FalkForge has**: `BundlePackageBuilder.Property("InstallArguments", "...")` is used in the MAS demo as a workaround. However, the actual EXE execution in the engine (`ExeExecutor`) needs to support formatted argument strings with variable resolution.

**What's missing**: Verification that `ExeExecutor` resolves `[VariableName]` references in `InstallArguments` before passing to `ProcessRunner`. The builder API works but the engine execution path needs confirmation.

**Suggested fix**:
1. Verify `ExeExecutor` reads `InstallArguments` from package properties and resolves variable references via `VariableStore`.
2. If not implemented, add variable resolution in `ExeExecutor.Execute()` before spawning the process.
3. Add integration tests for EXE packages with formatted argument strings.

**Affected projects**: `FalkForge.Engine` (Execution/ExeExecutor.cs, Variables/VariableStore.cs)

**Complexity**: Low-Medium (2-4 hours). May already work; needs verification and tests.

---

## Moderate Gaps

### 11. Shortcut Icon from File

**What WiX does**: `<Icon Id="KeyholeIcon" SourceFile="path/to/Keyhole.ico"/>` followed by `<Shortcut Icon="KeyholeIcon" .../>`. The .ico file is embedded in the MSI's `Icon` table.

**What FalkForge has**: `ShortcutBuilder.WithIcon()` exists. The gap in the MAS demo is that no `.ico` file is included in the demo payload, so the shortcut has no custom icon.

**What's missing**: Nothing in the framework -- just a demo payload file. This is a demo completeness issue, not a framework gap.

**Suggested fix**: Add `Keyhole.ico` to `demo/MAS/packages/MultiAccess/payload/` and wire it up via `.WithIcon("payload/Keyhole.ico")`.

**Complexity**: Trivial.

---

### 12. Bundle Variable UI Binding

**What WiX does**: Bundle variables declared with `<Variable>` are bidirectionally bound to the UI. The WiX managed bootstrapper application reads/writes variables via `IEngine.StringVariables["name"]`.

**What FalkForge has**: `BundleBuilder.Variable()` declares variables. `IInstallerEngine.SetProperty()` / `SetSecureProperty()` sends values to the engine. The custom UI pages in MAS collect values and call `Engine.SetProperty()`.

**What's missing**: This works today. The MAS demo successfully demonstrates variable binding through `SetProperty`. No framework gap -- just noting that the pattern works differently from WiX (explicit `SetProperty` calls vs. automatic variable binding).

**Complexity**: N/A -- already supported.

---

## Readability Comparison

### Service Installation

**WiX XML** (MultiServer/MSMsi/MSServerService.wxs):
```xml
<Fragment>
  <ComponentGroup Id="ServiceComponents" Directory="INSTALLFOLDER">
    <Component Id="ServiceInstallComp" Guid="{...}">
      <Condition>ASSERVICE ~= "true"</Condition>
      <File Id="MultiServerExe" Source="$(var.SourceDir)\MultiServer.exe" KeyPath="yes" />
      <ServiceInstall
        Id="MultiServerService"
        Name="[SERVICENAME]"
        DisplayName="[SERVICENAME]"
        Description="MultiServer service"
        Arguments="DSN=[ODBCNAME]"
        Account="[SERVICEACCOUNT]"
        Password="[SERVICEPASSWORD]"
        Start="auto"
        Type="ownProcess"
        ErrorControl="normal" />
      <ServiceControl
        Id="MultiServerControl"
        Name="[SERVICENAME]"
        Start="install"
        Stop="both"
        Remove="uninstall"
        Wait="no" />
      <util:PermissionEx
        User="Everyone"
        GenericAll="yes"
        ServiceChangeConfig="yes"
        ServiceStart="yes"
        ServiceStop="yes" />
    </Component>
  </ComponentGroup>
</Fragment>
```

**FalkForge C#** (MultiServer/Program.cs):
```csharp
package.Service("MultiServer", svc =>
{
    svc.DisplayName = "[SERVICENAME]";
    svc.Executable = "MultiServer.exe";
    svc.Description = "MultiServer service";
    svc.StartMode = ServiceStartMode.Automatic;
    svc.Account = ServiceAccount.User;
    svc.UserName = "[SERVICEACCOUNT]";
    svc.Password = "[SERVICEPASSWORD]";
});

package.ServiceControl(sc => sc
    .ServiceName("[SERVICENAME]")
    .StartOnInstall()
    .StopOnInstall()
    .StopOnUninstall()
    .DeleteOnUninstall()
    .Wait(false));
```

The FalkForge version is 14 lines vs 28 lines of XML. The fluent API eliminates GUIDs, Fragment/ComponentGroup boilerplate, and attribute verbosity.

---

### Registry Entries

**WiX XML** (MultiAccess/MASQLAttach.wxs):
```xml
<Fragment>
  <ComponentGroup Id="SQLAttachRegistryComponents" Directory="INSTALLFOLDER">
    <Component Id="SQLAttachRegistry" Guid="{...}">
      <RegistryKey Root="HKLM" Key="SOFTWARE\Aptus\SQLAttach">
        <RegistryValue Name="INSTALLFOLDER" Type="string" Value="[INSTALLFOLDER]" />
        <RegistryValue Name="INSTALLDB" Type="string" Value="[INSTALLDB]" />
        <RegistryValue Name="ATTACHDATABASE" Type="string" Value="[ATTACHDATABASE]" />
        <RegistryValue Name="DB_SERVER" Type="string" Value="[DB_SERVER]" />
        <!-- ... 12 more values ... -->
      </RegistryKey>
    </Component>
  </ComponentGroup>
</Fragment>
```

**FalkForge C#**:
```csharp
package.Registry(reg => reg
    .Key(RegistryRoot.LocalMachine, @"SOFTWARE\Aptus\SQLAttach", key => key
        .Value("INSTALLFOLDER", MsiProperty.Custom("INSTALLFOLDER"))
        .Value("INSTALLDB", MsiProperty.Custom("INSTALLDB"))
        .Value("ATTACHDATABASE", MsiProperty.Custom("ATTACHDATABASE"))
        .Value("DB_SERVER", MsiProperty.Custom("DB_SERVER"))
        // ... more values ...
    ));
```

No Fragment/ComponentGroup/Component/Guid ceremony. Values chain directly under the key.

---

### Feature Tree

**WiX XML**:
```xml
<Feature Id="MainApplication" Title="MultiAccess" Level="1">
  <ComponentGroupRef Id="MainApplicationFiles" />
  <ComponentGroupRef Id="ShortcutComponents" />
  <ComponentGroupRef Id="UtilityFiles" />
</Feature>
<Feature Id="Database" Title="Database" Level="1">
  <ComponentGroupRef Id="DatabaseFiles" />
</Feature>
```
(Plus separate `<ComponentGroup>` fragments for each file set.)

**FalkForge C#**:
```csharp
package.Feature("MainApplication", f =>
{
    f.Title = "MultiAccess";
    f.Files(files => files
        .Add("payload/MultiAccess.exe")
        .To(installFolder));
});

package.Feature("Database", f =>
{
    f.Title = "Database";
    f.Files(files => files
        .Add("payload/MultiAccess.mdf")
        .Add("payload/MultiAccess.ldf")
        .To(installFolder / "Database"));
});
```

Files are declared inline with the feature. No indirection through ComponentGroup references.

---

## Recommendations

Priority-ordered list of framework changes, grouped by implementation wave:

### Wave 1 -- Quick Wins (1-2 hours each, high impact)

| # | Gap | Change | Est. |
|---|-----|--------|------|
| 1 | ServiceModel.Arguments | Add property + builder method + table emission | 1-2h |
| 2 | Service Account Property Ref | Add `AccountProperty` string override | 1-2h |
| 9 | EnableFeatureSelection | Add flag to model/builder, emit in manifest | 1-2h |

### Wave 2 -- Component Attribute Gaps (2-4 hours each)

| # | Gap | Change | Est. |
|---|-----|--------|------|
| 3 | Service Component Condition | Add condition to ServiceModel, wire in ComponentResolver | 2-4h |
| 4 | NeverOverwrite / Permanent on File | Add flags to FileEntryModel, set component attribute bits | 2-4h |
| 7 | RegistrySearch builder API | Add SearchConditionBuilder.RegistrySearch() + engine eval | 2-4h |

### Wave 3 -- Bundle Engine Features (4-6 hours each)

| # | Gap | Change | Est. |
|---|-----|--------|------|
| 8 | Permanent Package | Model + builder + manifest + planner + executor | 4-6h |
| 10 | EXE Argument Variable Resolution | Verify/implement in ExeExecutor | 2-4h |
| 5 | Service Permissions | PermissionModel for ServiceInstall table + builder API | 3-5h |

### Wave 4 -- Infrastructure (8-16 hours)

| # | Gap | Change | Est. |
|---|-----|--------|------|
| 6 | PackageGroup Abstraction | New model/builder + built-in prerequisite definitions | 8-16h |

**Total estimated effort**: 25-47 hours across all waves.

Waves 1-2 would close 6 of 10 critical gaps with roughly 10-16 hours of work, covering the most common real-world service installer scenarios. Wave 3 adds bundle completeness. Wave 4 is the largest investment but provides the most user-facing value for enterprise installers with prerequisite chains.
