# FalkForge Validation Error Codes

Reference for every diagnostic code emitted by validators, builders, decompilers, and runtime integrity checks across the solution. Codes follow `<3 letters><3 digits>` (e.g. `PKG001`).

Auto-generated from source — do not edit by hand. To refresh: re-run the extraction script in `.tmp_work/`.

**Total codes:** 233 across 52 categories.

## Categories at a Glance

| Prefix | Category | Source |
|--------|----------|--------|
| ASM | Assembly | `src/FalkForge.Core/Validation` |
| BDC | Bundle decompiler | `src/FalkForge.Decompiler` |
| BDL | Bundle compiler | `src/FalkForge.Compiler.Bundle/Validation` |
| BDS | Bundle detacher (signing) | `src/FalkForge.Compiler.Bundle/Compilation` |
| CRF | CreateFolder | `src/FalkForge.Core/Validation` |
| CTB | Custom table | `src/FalkForge.Core/Validation` |
| DEC | MSI decompiler | `src/FalkForge.Decompiler` |
| DEP | Dependency extension | `src/FalkForge.Extensions.Dependency` |
| DLG | Dialog customization | `src/FalkForge.Compiler.Msi/UI` |
| DNG | Downgrade | `src/FalkForge.Core/Validation` |
| DPF | DuplicateFile | `src/FalkForge.Core/Validation` |
| DRV | Driver extension | `src/FalkForge.Extensions.Driver` |
| FAS | File association | `src/FalkForge.Core/Validation` |
| FEA | Feature | `src/FalkForge.Core/Validation` |
| FNT | Font | `src/FalkForge.Core/Validation` |
| FSH | FileShare (Util) | `src/FalkForge.Extensions.Util/FileShare` |
| FWL | Firewall extension | `src/FalkForge.Extensions.Firewall` |
| GRP | Group (UserManagement) | `src/FalkForge.Extensions.Util/UserManagement` |
| IIS | IIS extension | `src/FalkForge.Extensions.Iis` |
| INI | INI file | `src/FalkForge.Core/Validation` |
| INT | Integrity verifier | `src/FalkForge.Engine/Integrity` |
| ISC | InternetShortcut (Util) | `src/FalkForge.Extensions.Util/InternetShortcut` |
| JSN | JSON config loader | `src/FalkForge.Cli` |
| LOC | Localization | `src/FalkForge.Localization` |
| MDT | Media template | `src/FalkForge.Core/Validation` |
| MSM | Merge module (MSM) | `src/FalkForge.Core/Validation` |
| MSP | Patch (MSP) | `src/FalkForge.Core/Validation` |
| MST | Transform (MST) | `src/FalkForge.Core/Validation` |
| MUP | MajorUpgrade | `src/FalkForge.Core/Validation` |
| MVF | MoveFile | `src/FalkForge.Core/Validation` |
| NET | .NET search | `src/FalkForge.Extensions.DotNet` |
| PKG | Package model | `src/FalkForge.Core/Validation` |
| PRM | Permissions | `src/FalkForge.Core/Validation` |
| QEX | QuietExec (Util) | `src/FalkForge.Extensions.Util/QuietExec` |
| REG | Registry | `src/FalkForge.Core/Validation` |
| RFX | RemoveFolderEx (Util) | `src/FalkForge.Extensions.Util/RemoveFolderEx` |
| RMF | RemoveFile | `src/FalkForge.Core/Validation` |
| RPR | Reproducible build | `src/FalkForge.Compiler.Bundle/Builders` |
| RRG | RemoveRegistry | `src/FalkForge.Core/Validation` |
| SCT | Service control | `src/FalkForge.Core/Validation` |
| SDP | Service dependency | `src/FalkForge.Core/Validation` |
| SGN | Signing | `src/FalkForge.Core/Validation` |
| SHA | SHA hashing | `src/FalkForge.Core/Sbom` |
| SHC | Shortcut | `src/FalkForge.Core/Validation` |
| SQL | SQL extension | `src/FalkForge.Extensions.Sql` |
| STU | Studio | `src/FalkForge.Studio/Shell` |
| SVC | Service install | `src/FalkForge.Core/Validation` |
| UPD | Update feed | `src/FalkForge.Engine` |
| USR | User (UserManagement) | `src/FalkForge.Extensions.Util/UserManagement` |
| WBD | WiX Burn decompiler | `src/FalkForge.Decompiler` |
| WMM | WiX manifest mapper | `src/FalkForge.Decompiler` |
| XCF | XmlConfig (Util) | `src/FalkForge.Extensions.Util/XmlConfig` |

## ASM — Assembly

| Code | Message | Source |
|------|---------|--------|
| ASM001 | Assembly FileRef is required | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| ASM002 | GAC assembly should have a PublicKeyToken (warning) | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| ASM003 | Assembly version must match x.x.x.x format | `src/FalkForge.Core/Validation/RemainingRules.cs` |

## BDC — Bundle decompiler

| Code | Message | Source |
|------|---------|--------|
| BDC001 | Cannot open bundle file '<x>'. File not found | `src/FalkForge.Decompiler/BundleDecompiler.cs` |
| BDC002 | Bundle magic marker not found within maximum scan distance (BDC002) | `src/FalkForge.Decompiler/BundleAccess.cs` |
| BDC003 | Failed to deserialize manifest: null result (BDC003) | `src/FalkForge.Decompiler/BundleAccess.cs` |
| BDC004 | Invalid bundle: footer magic mismatch (BDC004) | `src/FalkForge.Decompiler/BundleAccess.cs` |

## BDL — Bundle compiler

| Code | Message | Source |
|------|---------|--------|
| BDL001 | Name is required         if (string.IsNullOrWhiteSpace(model.Name)) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL002 | Manufacturer is required         if (string.IsNullOrWhiteSpace(model.Manufacturer)) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL003 | Version must be valid         if (!Version.TryParse(model.Version, out _)) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL004 | At least one package required         if (model.Packages.Count == 0) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL005 | Package IDs must be unique         var duplicateIds = model.Packages | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL006 | {string.Join( | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL007 | Custom UI requires a project path         if (model.UiConfig is <x> uiConfig && | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL008 | BundleId must not be empty         if (model.BundleId == Guid.Empty) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL009 | UpgradeCode must not be empty         if (model.UpgradeCode == Guid.Empty) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL010 | Variable name is required         foreach (var variable in model.Variables) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL011 | Variable names must be unique         var duplicateVarNames = model.Variables | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL012 | Default value must match variable type         foreach (var variable in model.Variables) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL013 | Secret variable cannot be Persisted         foreach (var variable in model.Variables) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL014 | Feature ID is required         foreach (var feature in model.Features) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL015 | Feature IDs must be unique         var duplicateFeatureIds = model.Features | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL016 | Feature '<x>' references unknown package IDs: {string.Join( | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL017 | Required feature must have at least one package         foreach (var feature in model.Features) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL018 | Feature title is required         foreach (var feature in model.Features) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL019 | Dependency provider key must not be empty         foreach (var provider in model.DependencyProviders) | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL020 | Dependency provider '<x>' has invalid version '<x>' | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL021 | Duplicate dependency provider keys         var duplicateProviderKeys = model.DependencyProviders | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL022 | Dependency consumer provider key must not be empty | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL023 | Dependency consumer key must not be empty | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL024 | Update feed URL '<x>' is not a valid absolute URI | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL025 | Update feed URL must use HTTPS scheme, got '<x>' | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |
| BDL027 | EnableFeatureSelection is only valid for MsiPackage type, but package '<x>' is <x> | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` |

## BDS — Bundle detacher (signing)

| Code | Message | Source |
|------|---------|--------|
| BDS001 | Not a valid FALKBUNDLE — footer magic not found | `src/FalkForge.Compiler.Bundle/Compilation/BundleDetacher.cs` |
| BDS002 | Data file is corrupted — magic marker not found after header | `src/FalkForge.Compiler.Bundle/Compilation/BundleDetacher.cs` |
| BDS003 | Reattach verification failed — payload '<x>' offset out of bounds | `src/FalkForge.Compiler.Bundle/Compilation/BundleDetacher.cs` |

## CRF — CreateFolder

| Code | Message | Source |
|------|---------|--------|
| CRF001 | CreateFolder DirectoryRef is required | `src/FalkForge.Core/Validation/MiscRules.FileOps.cs` |

## CTB — Custom table

| Code | Message | Source |
|------|---------|--------|
| CTB001 | Custom table Name is required | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB002 | Custom table name must not exceed 31 characters | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB003 | Custom table name must start with a letter and contain only alphanumeric/underscore | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB004 | Custom table must have at least one column | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB005 | Column Name is required | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB006 | Column names must be unique within a table | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB007 | Custom table must have at least one primary key column | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB008 | Row must not reference an unknown column | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB009 | Row value type must match the column's declared type | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB010 | Column name must start with a letter or underscore and contain only alphanumeric/underscore | `src/FalkForge.Core/Validation/CustomTableRules.cs` |
| CTB011 | Row string value references a sensitive MSI property (warning) | `src/FalkForge.Core/Validation/CustomTableRules.cs` |

## DEC — MSI decompiler

| Code | Message | Source |
|------|---------|--------|
| DEC001 | Cannot open MSI file '<x>'. <x> | `src/FalkForge.Decompiler/MsiTableAccess.cs` |
| DEC003 | Table '<x>' row <x> column '<x>' | `src/FalkForge.Decompiler/Recipe/ReadRow.cs` |

## DEP — Dependency extension

| Code | Message | Source |
|------|---------|--------|
| DEP001 | Dependency provider key must not be empty | `src/FalkForge.Extensions.Dependency/DependencyValidator.cs` |
| DEP002 | Dependency provider '<x>' has invalid version '<x>'. Must be a valid System.Version | `src/FalkForge.Extensions.Dependency/DependencyValidator.cs` |
| DEP003 | Dependency consumer must reference a non-empty provider key | `src/FalkForge.Extensions.Dependency/DependencyValidator.cs` |
| DEP004 | Dependency consumer for provider '<x>' has MinVersion '<x>' greater than MaxVersion '<x>' | `src/FalkForge.Extensions.Dependency/DependencyValidator.cs` |
| DEP005 | Duplicate dependency provider key '<x>' | `src/FalkForge.Extensions.Dependency/DependencyValidator.cs` |
| DEP006 | Dependency key '<x>' contains invalid characters (backslash, forward slash, or null) | `src/FalkForge.Extensions.Dependency/DependencyValidator.cs` |
| DEP007 | Dependency consumer key must not be empty | `src/FalkForge.Extensions.Dependency/DependencyValidator.cs` |

## DLG — Dialog customization

| Code | Message | Source |
|------|---------|--------|
| DLG001 | every InsertedStep name must be registered.         foreach (var step in customization.InsertedSteps) | `src/FalkForge.Compiler.Msi/UI/DialogCustomizationValidator.cs` |
| DLG002 | Cannot suppress '<x>' dialog in the <x> template: | `src/FalkForge.Compiler.Msi/UI/DialogCustomizationValidator.cs` |

## DNG — Downgrade

| Code | Message | Source |
|------|---------|--------|
| DNG001 | Downgrade.Block() requires an error message | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| DNG002 | Downgrade configuration requires MajorUpgrade | `src/FalkForge.Core/Validation/RemainingRules.cs` |

## DPF — DuplicateFile

| Code | Message | Source |
|------|---------|--------|
| DPF001 | DuplicateFile FileRef is required | `src/FalkForge.Core/Validation/MiscRules.FileOps.cs` |

## DRV — Driver extension

| Code | Message | Source |
|------|---------|--------|
| DRV001 | Driver Id must not be empty | `src/FalkForge.Extensions.Driver/DriverValidator.cs` |
| DRV002 | Driver '<x>' must have an InfFilePath | `src/FalkForge.Extensions.Driver/DriverValidator.cs` |
| DRV003 | Driver '<x>' InfFilePath must end with '.inf' | `src/FalkForge.Extensions.Driver/DriverValidator.cs` |
| DRV004 | Duplicate driver Id '<x>' | `src/FalkForge.Extensions.Driver/DriverValidator.cs` |

## FAS — File association

| Code | Message | Source |
|------|---------|--------|
| FAS001 | File association Extension is required | `src/FalkForge.Core/Validation/MiscRules.Shortcuts.cs` |
| FAS002 | File association ProgId is required | `src/FalkForge.Core/Validation/MiscRules.Shortcuts.cs` |
| FAS003 | File association has no verbs (warning) | `src/FalkForge.Core/Validation/MiscRules.Shortcuts.cs` |

## FEA — Feature

| Code | Message | Source |
|------|---------|--------|
| FEA001 | Feature Id is required | `src/FalkForge.Core/Validation/FeatureRules.cs` |
| FEA002 | Feature Id must be unique across the feature tree | `src/FalkForge.Core/Validation/FeatureRules.cs` |
| FEA003 | Feature Title is required | `src/FalkForge.Core/Validation/FeatureRules.cs` |
| FEA004 | Feature condition expression must not be empty | `src/FalkForge.Core/Validation/FeatureRules.cs` |
| FEA005 | Feature condition level must not be negative | `src/FalkForge.Core/Validation/FeatureRules.cs` |

## FNT — Font

| Code | Message | Source |
|------|---------|--------|
| FNT001 | Font FileName is required | `src/FalkForge.Core/Validation/MiscRules.Shortcuts.cs` |

## FSH — FileShare (Util)

| Code | Message | Source |
|------|---------|--------|
| FSH001 | FileShare Id is required | `src/FalkForge.Extensions.Util/FileShare/FileShareBuilder.cs` |
| FSH002 | FileShare Name is required | `src/FalkForge.Extensions.Util/FileShare/FileShareBuilder.cs` |
| FSH003 | FileShare Directory is required | `src/FalkForge.Extensions.Util/FileShare/FileShareBuilder.cs` |

## FWL — Firewall extension

| Code | Message | Source |
|------|---------|--------|
| FWL001 | Firewall rule '<x>' must have a Name | `src/FalkForge.Extensions.Firewall/FirewallValidator.cs` |
| FWL002 | Firewall rule '<x>' must specify either a Port or a Program | `src/FalkForge.Extensions.Firewall/FirewallValidator.cs` |
| FWL003 | Firewall rule '<x>' has invalid <x> format '<x>'. Expected a number (e.g. '8080') or a range (e.g. '8080-8090') | `src/FalkForge.Extensions.Firewall/FirewallValidator.cs` |
| FWL004 | Duplicate firewall rule Id '<x>' | `src/FalkForge.Extensions.Firewall/FirewallValidator.cs` |

## GRP — Group (UserManagement)

| Code | Message | Source |
|------|---------|--------|
| GRP001 | Group Name is required | `src/FalkForge.Extensions.Util/UserManagement/GroupBuilder.cs` |

## IIS — IIS extension

| Code | Message | Source |
|------|---------|--------|
| IIS001 | WebSite must have a Description | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS002 | WebSite '<x>' must have at least one binding | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS003 | Binding on site '<x>' must have a valid Port (1-65535) | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS004 | HTTPS binding on site '<x>' must have a CertificateRef | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS005 | AppPool must have a Name | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS006 | AppPool '<x>' uses SpecificUser identity but no UserName specified | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS007 | Certificate must have an Id | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS008 | Certificate '<x>' must have a FindValue | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS009 | AppPool '<x>' uses SpecificUser identity but no Password specified | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS010 | WebApplication '<x>' on site '<x>' references undefined app pool '<x>' | `src/FalkForge.Extensions.Iis/IisValidator.cs` |
| IIS011 | Binding on site '<x>' references undefined certificate '<x>' | `src/FalkForge.Extensions.Iis/IisValidator.cs` |

## INI — INI file

| Code | Message | Source |
|------|---------|--------|
| INI001 | INI file FileName is required | `src/FalkForge.Core/Validation/MiscRules.IniPermissions.cs` |
| INI002 | INI file Section is required | `src/FalkForge.Core/Validation/MiscRules.IniPermissions.cs` |
| INI003 | INI file Key is required | `src/FalkForge.Core/Validation/MiscRules.IniPermissions.cs` |

## INT — Integrity verifier

| Code | Message | Source |
|------|---------|--------|
| INT001 | Manifest signature verification failed. The installer may have been tampered with | `src/FalkForge.Engine/Integrity/IntegrityVerifier.cs` |
| INT002 | Payload file '<x>' not found. The installer may be incomplete or tampered with | `src/FalkForge.Engine/Integrity/IntegrityVerifier.cs` |
| INT003 | Manifest signature envelope is missing required fields (publicKey or signature) | `src/FalkForge.Engine/Integrity/IntegrityVerifier.cs` |

## ISC — InternetShortcut (Util)

| Code | Message | Source |
|------|---------|--------|
| ISC001 | InternetShortcut Id is required | `src/FalkForge.Extensions.Util/InternetShortcut/InternetShortcutBuilder.cs` |
| ISC002 | InternetShortcut Name is required | `src/FalkForge.Extensions.Util/InternetShortcut/InternetShortcutBuilder.cs` |
| ISC003 | InternetShortcut Target URL is required | `src/FalkForge.Extensions.Util/InternetShortcut/InternetShortcutBuilder.cs` |
| ISC004 | InternetShortcut Directory is required | `src/FalkForge.Extensions.Util/InternetShortcut/InternetShortcutBuilder.cs` |

## JSN — JSON config loader

| Code | Message | Source |
|------|---------|--------|
| JSN001 | Invalid JSON: <x> | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN002 | Missing required field: product.name | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN003 | Missing required field: product.manufacturer | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN004 | Invalid version format: <x> | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN005 | Invalid upgrade code GUID: <x> | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN006 | Invalid UI dialog set: <x>. Valid values: None, Minimal, InstallDir, FeatureTree, Mondo, Advanced | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN007 | Invalid platform: <x>. Valid values: X86, X64, Arm64 | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN009 | Feature must have an id | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN010 | Configuration error: <x> | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN011 | Firewall rule '<x>' must specify either 'port' or 'program' | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN012 | IIS web site at index <x> is missing required field 'description' | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN013 | SQL configuration at index <x> is missing required field 'server' | `src/FalkForge.Cli/JsonConfigLoader.cs` |
| JSN014 | .NET detection at index <x> is missing required field 'minimumVersion' | `src/FalkForge.Cli/JsonConfigLoader.cs` |

## LOC — Localization

| Code | Message | Source |
|------|---------|--------|
| LOC001 | Duplicate string ID '<x>' in culture '<x>' | `src/FalkForge.Localization/LocalizationBuilder.cs` |
| LOC002 | Default culture '<x>' is not defined. Add it with AddCulture() or AddJsonFile() | `src/FalkForge.Localization/LocalizationBuilder.cs` |
| LOC003 | Unresolved localization reference '!(loc.<x>)'. String ID '<x>' not found in any culture | `src/FalkForge.Localization/LocalizedStringResolver.cs` |
| LOC004 | Cannot extract culture from filename. Expected format: name.culture.json (e.g., strings.en-US.json) | `src/FalkForge.Localization/LocalizationLoader.cs` |

## MDT — Media template

| Code | Message | Source |
|------|---------|--------|
| MDT001 | MediaTemplate CabinetTemplate is required | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| MDT002 | CabinetTemplate must contain the <x> placeholder | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| MDT003 | MaximumCabinetSizeInMB must not be negative | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| MDT004 | MaximumUncompressedMediaSize must not be negative | `src/FalkForge.Core/Validation/RemainingRules.cs` |

## MSM — Merge module (MSM)

| Code | Message | Source |
|------|---------|--------|
| MSM001 | Merge module Id must not be empty | `src/FalkForge.Core/Validation/MergeModuleValidator.cs` |
| MSM002 | Merge module Version must not be null | `src/FalkForge.Core/Validation/MergeModuleValidator.cs` |
| MSM003 | Merge module Language must not be zero | `src/FalkForge.Core/Validation/MergeModuleValidator.cs` |
| MSM004 | Merge module Manufacturer must not be empty | `src/FalkForge.Core/Validation/MergeModuleValidator.cs` |

## MSP — Patch (MSP)

| Code | Message | Source |
|------|---------|--------|
| MSP001 | Patch TargetMsiPath is required | `src/FalkForge.Core/Validation/PatchValidator.cs` |
| MSP002 | Patch UpdatedMsiPath is required | `src/FalkForge.Core/Validation/PatchValidator.cs` |
| MSP003 | Patch Classification must be a defined enum value | `src/FalkForge.Core/Validation/PatchValidator.cs` |
| MSP004 | Patch Id must not be empty GUID | `src/FalkForge.Core/Validation/PatchValidator.cs` |

## MST — Transform (MST)

| Code | Message | Source |
|------|---------|--------|
| MST001 | Transform BaseMsiPath is required | `src/FalkForge.Core/Validation/TransformValidator.cs` |
| MST002 | Transform TargetMsiPath is required | `src/FalkForge.Core/Validation/TransformValidator.cs` |

## MUP — MajorUpgrade

| Code | Message | Source |
|------|---------|--------|
| MUP001 | MajorUpgrade requires UpgradeCode | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| MUP003 | MajorUpgrade and Upgrade table entries cannot coexist | `src/FalkForge.Core/Validation/RemainingRules.cs` |

## MVF — MoveFile

| Code | Message | Source |
|------|---------|--------|
| MVF001 | MoveFile SourceDirectory is required | `src/FalkForge.Core/Validation/MiscRules.FileOps.cs` |
| MVF002 | MoveFile SourceFileName is required | `src/FalkForge.Core/Validation/MiscRules.FileOps.cs` |
| MVF003 | MoveFile DestDirectory is required | `src/FalkForge.Core/Validation/MiscRules.FileOps.cs` |

## NET — .NET search

| Code | Message | Source |
|------|---------|--------|
| NET001 | VariableName is required | `src/FalkForge.Extensions.DotNet/DotNetSearchValidator.cs` |
| NET002 | MinimumVersion is required | `src/FalkForge.Extensions.DotNet/DotNetSearchValidator.cs` |
| NET003 | Duplicate VariableName '<x>' | `src/FalkForge.Extensions.DotNet/DotNetSearchValidator.cs` |

## PKG — Package model

| Code | Message | Source |
|------|---------|--------|
| PKG001 | Package Name is required | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG002 | Package Manufacturer is required | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG003 | Package Version is required | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG004 | Package Version is 0.0.0 | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG005 | MSI major version cannot exceed 255 | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG006 | MSI minor version cannot exceed 255 | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG007 | MSI build version cannot exceed 65535 | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG008 | Package Name exceeds 64 characters, which may cause display issues | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG009 | UpgradeCode must not be empty GUID | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG010 | ProductCode must not be empty GUID | `src/FalkForge.Core/Validation/PackageRules.cs` |
| PKG011 | Package has no files. The MSI will be empty | `src/FalkForge.Core/Validation/PackageRules.cs` |

## PRM — Permissions

| Code | Message | Source |
|------|---------|--------|
| PRM001 | Permission LockObject is required | `src/FalkForge.Core/Validation/MiscRules.IniPermissions.cs` |
| PRM002 | Permission must have either SDDL or User | `src/FalkForge.Core/Validation/MiscRules.IniPermissions.cs` |
| PRM003 | Permission Table must be a valid MSI table name | `src/FalkForge.Core/Validation/MiscRules.IniPermissions.cs` |
| PRM004 | Cannot mix SDDL and User/Domain permissions in the same package | `src/FalkForge.Core/Validation/MiscRules.IniPermissions.cs` |

## QEX — QuietExec (Util)

| Code | Message | Source |
|------|---------|--------|
| QEX001 | QuietExec Id is required | `src/FalkForge.Extensions.Util/QuietExec/QuietExecBuilder.cs` |
| QEX002 | QuietExec CommandLine is required | `src/FalkForge.Extensions.Util/QuietExec/QuietExecBuilder.cs` |
| QEX003 | QuietExec CommandLine exceeds maximum length of <x> characters | `src/FalkForge.Extensions.Util/QuietExec/QuietExecBuilder.cs` |
| QEX004 | QuietExec RollbackCommandLine exceeds maximum length of <x> characters | `src/FalkForge.Extensions.Util/QuietExec/QuietExecBuilder.cs` |

## REG — Registry

| Code | Message | Source |
|------|---------|--------|
| REG007 | Registry value references a sensitive MSI property (warning) | `src/FalkForge.Core/Validation/MiscRules.Registry.cs` |

## RFX — RemoveFolderEx (Util)

| Code | Message | Source |
|------|---------|--------|
| RFX001 | RemoveFolderEx Id is required | `src/FalkForge.Extensions.Util/RemoveFolderEx/RemoveFolderExBuilder.cs` |
| RFX002 | RemoveFolderEx requires either Directory or Property | `src/FalkForge.Extensions.Util/RemoveFolderEx/RemoveFolderExBuilder.cs` |

## RMF — RemoveFile

| Code | Message | Source |
|------|---------|--------|
| RMF001 | RemoveFile DirectoryRef is required | `src/FalkForge.Core/Validation/MiscRules.FileOps.cs` |
| RMF002 | RemoveFile must specify at least OnInstall or OnUninstall | `src/FalkForge.Core/Validation/MiscRules.FileOps.cs` |

## RPR — Reproducible build

| Code | Message | Source |
|------|---------|--------|
| RPR001 | SOURCE_DATE_EPOCH '<x>' is not a valid Unix timestamp | `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs` |
| RPR002 | SOURCE_DATE_EPOCH is not set and no explicit epoch was provided | `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs` |

## RRG — RemoveRegistry

| Code | Message | Source |
|------|---------|--------|
| RRG001 | RemoveRegistry Id is required | `src/FalkForge.Core/Validation/MiscRules.Registry.cs` |
| RRG002 | RemoveRegistry Key is required | `src/FalkForge.Core/Validation/MiscRules.Registry.cs` |
| RRG003 | RemoveRegistryEntries | `src/FalkForge.Core/Validation/MiscRules.Registry.cs` |

## SCT — Service control

| Code | Message | Source |
|------|---------|--------|
| SCT001 | ServiceControl ServiceName is required | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SCT002 | ServiceControl must have at least one event | `src/FalkForge.Core/Validation/ServiceRules.cs` |

## SDP — Service dependency

| Code | Message | Source |
|------|---------|--------|
| SDP001 | Service dependency DependsOn value is required | `src/FalkForge.Core/Validation/ServiceRules.cs` |

## SGN — Signing

| Code | Message | Source |
|------|---------|--------|
| SGN001 | PFX certificate embeds private key (warning) | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| SGN002 | Signing requires CertificatePath or CertificateThumbprint | `src/FalkForge.Core/Validation/RemainingRules.cs` |
| SGN003 | DigestAlgorithm must be sha256, sha384, or sha512 | `src/FalkForge.Core/Validation/RemainingRules.cs` |

## SHA — SHA hashing

| Code | Message | Source |
|------|---------|--------|
| SHA256 | );                 writer.WriteString( | `src/FalkForge.Core/Sbom/IntegritySbomGenerator.cs` |

## SHC — Shortcut

| Code | Message | Source |
|------|---------|--------|
| SHC001 | Shortcut Name is required | `src/FalkForge.Core/Validation/MiscRules.Shortcuts.cs` |
| SHC002 | Shortcut TargetFile is required | `src/FalkForge.Core/Validation/MiscRules.Shortcuts.cs` |
| SHC003 | Shortcut has no locations (warning) | `src/FalkForge.Core/Validation/MiscRules.Shortcuts.cs` |

## SQL — SQL extension

| Code | Message | Source |
|------|---------|--------|
| SQL001 | Database requires either Server or ConnectionString | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL002 | Script requires a DatabaseRef | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL003 | Script must specify either SourceFile or SqlContent, not both | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL004 | Database name is required | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL005 | SqlString requires a Sql statement | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL006 | Duplicate database Id '<x>' | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL007 | Duplicate script Id '<x>' | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL008 | Duplicate SqlString Id '<x>' | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL009 | Script SqlContent exceeds maximum length of <x> characters | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL010 | SqlString Sql exceeds maximum length of <x> characters | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL011 | Database Id is required | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL012 | Script Id is required | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |
| SQL013 | SqlString Id is required | `src/FalkForge.Extensions.Sql/SqlValidator.cs` |

## STU — Studio

| Code | Message | Source |
|------|---------|--------|
| STU001 | , Severity = | `src/FalkForge.Studio/Shell/StudioViewModel.Validation.cs` |
| STU002 | , Severity = | `src/FalkForge.Studio/Shell/StudioViewModel.Validation.cs` |
| STU003 | , Severity = | `src/FalkForge.Studio/Shell/StudioViewModel.Validation.cs` |
| STU004 | , Severity = | `src/FalkForge.Studio/Shell/StudioViewModel.Validation.cs` |
| STU005 | , Severity = | `src/FalkForge.Studio/Shell/StudioViewModel.Validation.cs` |
| STU006 | , Severity = | `src/FalkForge.Studio/Shell/StudioViewModel.Validation.cs` |
| STU099 | , Severity = | `src/FalkForge.Studio/Shell/StudioViewModel.Validation.cs` |

## SVC — Service install

| Code | Message | Source |
|------|---------|--------|
| SVC001 | Service Name is required | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SVC002 | Service Executable is required | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SVC003 | User account requires UserName | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SVC004 | Service name must not exceed 256 characters | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SVC005 | Plaintext password warning | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SVC009 | Empty Arguments string should be null | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SVC010 | AccountProperty conflicts with UserName | `src/FalkForge.Core/Validation/ServiceRules.cs` |
| SVC011 | Empty ComponentCondition must be null | `src/FalkForge.Core/Validation/ServiceRules.cs` |

## UPD — Update feed

| Code | Message | Source |
|------|---------|--------|
| UPD001 | Failed to fetch update feed: HTTP <x> | `src/FalkForge.Engine/Download/UpdateChecker.cs` |
| UPD002 | Failed to parse update feed: <x> | `src/FalkForge.Engine/Download/UpdateFeedParser.cs` |
| UPD003 | Update feed bundle ID mismatch. Expected <x>, got <x> | `src/FalkForge.Engine/Download/UpdateFeedParser.cs` |
| UPD004 | Update entry download URL is not a valid HTTPS URI: '<x>' | `src/FalkForge.Engine/Download/UpdateFeedParser.cs` |
| UPD005 | Update file not found: '<x>' | `src/FalkForge.Engine/UpdateLauncher.cs` |

## USR — User (UserManagement)

| Code | Message | Source |
|------|---------|--------|
| USR001 | User Name is required | `src/FalkForge.Extensions.Util/UserManagement/UserValidator.cs` |
| USR002 | Password is required for new user creation (when UpdateIfExists is false).     /// | `src/FalkForge.Extensions.Util/UserManagement/UserValidator.cs` |

## WBD — WiX Burn decompiler

| Code | Message | Source |
|------|---------|--------|
| WBD001 | Bundle file not found: <x> | `src/FalkForge.Decompiler/WixBurnAccess.cs` |
| WBD002 | Invalid PE header: e_lfanew out of range | `src/FalkForge.Decompiler/WixBurnAccess.cs` |
| WBD003 | PE file does not contain a .wixburn section | `src/FalkForge.Decompiler/WixBurnAccess.cs` |
| WBD004 | Invalid .wixburn magic: expected 0x<x>, found 0x<x> | `src/FalkForge.Decompiler/WixBurnAccess.cs` |
| WBD005 | Failed to extract UX container cabinet: <x> | `src/FalkForge.Decompiler/WixBurnAccess.cs` |
| WBD006 | Manifest file not found in UX container cabinet | `src/FalkForge.Decompiler/WixBurnAccess.cs` |

## WMM — WiX manifest mapper

| Code | Message | Source |
|------|---------|--------|
| WMM001 | Manifest XML has no root element | `src/FalkForge.Decompiler/WixManifestMapper.cs` |

## XCF — XmlConfig (Util)

| Code | Message | Source |
|------|---------|--------|
| XCF001 | XPath expression must not be empty | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF002 | FilePath must not be empty | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF003 | CreateElement action requires ElementName | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF004 | SetAttribute action requires AttributeName and Value | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF005 | XPath expression exceeds maximum length of <x> characters | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF006 | DeleteAttribute action requires AttributeName | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF007 | SetValue action requires Value | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF008 | BulkSetValue action requires Value | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |
| XCF009 | Duplicate XmlConfig Id '<x>' | `src/FalkForge.Extensions.Util/XmlConfig/XmlConfigValidator.cs` |

## Documented but not found in source

Codes referenced in CLAUDE.md / docs but with no current source occurrence (may have been removed, renumbered, or not yet implemented):

- `DEC002`
- `JSN008`
- `REG001`
- `REG002`
- `REG003`
- `REG004`
- `REG005`
- `REG006`
- `SVC006`
- `SVC007`
- `SVC008`
