# WiX Parity Gaps Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close all 10 framework gaps preventing 1:1 WiX parity for the MAS demo.

**Architecture:** Additive changes to existing models, builders, emitters, and engine components. Each gap adds init-only properties to models, fluent methods to builders, emission logic to TableEmitter/ManifestGenerator, and validation rules. No breaking changes to existing APIs.

**Tech Stack:** C# 13, .NET 10, xUnit 2.9.3, FalkForge Core/Compiler.Msi/Compiler.Bundle/Engine

**Branch:** Work in `.worktrees/gap-fixes/` — all paths below are relative to `D:/Git/FalkInstaller/.worktrees/gap-fixes/`

---

## TASK 1: ServiceModel.Arguments

**Gap:** ServiceInstall table supports an Arguments column but FalkForge ignores it.

**Files to modify:**
- `src/FalkForge.Core/Models/ServiceModel.cs` — add `string? Arguments` (init-only)
- `src/FalkForge.Core/Builders/ServiceBuilder.cs` — add `string? Arguments { get; set; }` + fluent `Arguments(string args)`
- `src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs` — in `EmitServices()` (~line 587): emit `service.Arguments` instead of `null`
- `src/FalkForge.Core/Validation/ModelValidator.cs` — add SVC009: warn if Arguments is empty string (not null)

**Tests to create/extend:**
- `tests/FalkForge.Core.Tests/Builders/ServiceBuilderTests.cs` (directory exists but file does not — create)
- `tests/FalkForge.Compiler.Msi.Tests/ServiceTableEmissionTests.cs` (create)

### Step 1: Write failing tests

Create `tests/FalkForge.Core.Tests/Builders/ServiceBuilderTests.cs`:

```csharp
// Tests:
// 1. Arguments_SetsPropertyOnModel — builder.Arguments("--port 8080") sets model.Arguments
// 2. Arguments_DefaultIsNull — model.Arguments is null by default
// 3. SVC009_EmptyArguments_ProducesWarning — validation warns on empty string ""
// 4. SVC009_NullArguments_NoWarning — validation does not warn on null
```

Create `tests/FalkForge.Compiler.Msi.Tests/ServiceTableEmissionTests.cs`:

```csharp
// Tests:
// 1. EmitServices_WithArguments_WritesArgumentsColumn — verify MsiRecord receives arguments value
// 2. EmitServices_WithoutArguments_WritesNullColumn — verify null is written when no arguments
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServiceBuilderTests" --no-restore
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~ServiceTableEmissionTests" --no-restore
```

### Step 3: Implement

1. Add `public string? Arguments { get; init; }` to `ServiceModel`
2. Add `public string? Arguments { get; set; }` and `public ServiceBuilder Arguments(string args)` to `ServiceBuilder`, wire in `Build()`
3. In `TableEmitter.EmitServices()`: replace `null` argument emission with `service.Arguments`
4. In `ModelValidator`: add SVC009 rule — if `Arguments` is `""` (empty, not null), emit warning

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServiceBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~ServiceTableEmissionTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(core): add ServiceModel.Arguments with SVC009 validation"
```

---

## TASK 2: Service AccountProperty

**Gap:** WiX allows binding service account to an MSI property (runtime-determined). FalkForge hardcodes account from enum.

**Files to modify:**
- `src/FalkForge.Core/Models/ServiceModel.cs` — add `string? AccountProperty` (init-only)
- `src/FalkForge.Core/Builders/ServiceBuilder.cs` — add `AccountProperty(string propertyRef)` returning `this`
- `src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs` — in `EmitServices()`: if `service.AccountProperty` is not null, emit it as StartName instead of enum-derived value
- `src/FalkForge.Core/Validation/ModelValidator.cs` — add SVC010: AccountProperty and Account=User with UserName are mutually exclusive

**Tests to extend:**
- `tests/FalkForge.Core.Tests/Builders/ServiceBuilderTests.cs`

### Step 1: Write failing tests

```csharp
// Tests:
// 1. AccountProperty_SetsPropertyOnModel — builder.AccountProperty("[SVCACCOUNT]") sets model
// 2. AccountProperty_DefaultIsNull — null by default
// 3. SVC010_AccountPropertyWithUserName_ProducesError — mutual exclusion validation
// 4. SVC010_AccountPropertyAlone_NoError — no error when used alone
// 5. EmitServices_WithAccountProperty_EmitsPropertyAsStartName — TableEmitter emits property ref
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServiceBuilderTests"
```

### Step 3: Implement

1. Add `public string? AccountProperty { get; init; }` to `ServiceModel`
2. Add fluent method to `ServiceBuilder`, wire in `Build()`
3. In `TableEmitter.EmitServices()`: check `service.AccountProperty` first; if set, emit that as StartName
4. In `ModelValidator`: add SVC010 — error if both `AccountProperty` and `UserName` are set

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServiceBuilderTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(core): add ServiceModel.AccountProperty with SVC010 validation"
```

---

## TASK 3: Service ComponentCondition

**Gap:** WiX supports conditions on the component that hosts a service executable. FalkForge has no way to conditionally install a service component.

**Files to modify:**
- `src/FalkForge.Core/Models/ServiceModel.cs` — add `string? ComponentCondition` (init-only)
- `src/FalkForge.Core/Builders/ServiceBuilder.cs` — add `Condition(string condition)` and `Condition(Condition condition)` overloads
- `src/FalkForge.Compiler.Msi/ComponentResolver.cs` — when resolving service file component, if `ServiceModel.ComponentCondition` is set, store for Condition table emission
- `src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs` — emit Condition table row for service component
- `src/FalkForge.Core/Validation/ModelValidator.cs` — add SVC011: condition must not be empty string

**Tests to extend/create:**
- `tests/FalkForge.Core.Tests/Builders/ServiceBuilderTests.cs`
- `tests/FalkForge.Compiler.Msi.Tests/ComponentResolverTests.cs` (exists — extend)

### Step 1: Write failing tests

```csharp
// ServiceBuilderTests:
// 1. Condition_String_SetsComponentCondition — builder.Condition("INSTALL_SERVICE") sets model
// 2. Condition_TypedCondition_SetsComponentCondition — builder.Condition(MsiProperty.Custom("X") == "1") sets model
// 3. SVC011_EmptyCondition_ProducesWarning — validation warns on ""
// 4. Condition_DefaultIsNull — null by default

// ComponentResolverTests:
// 5. ResolveComponent_ServiceWithCondition_IncludesConditionEntry
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServiceBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~ComponentResolverTests"
```

### Step 3: Implement

1. Add `public string? ComponentCondition { get; init; }` to `ServiceModel`
2. Add `Condition(string)` and `Condition(Condition)` to `ServiceBuilder`, wire in `Build()`
3. In `ComponentResolver`: propagate condition to resolved component
4. In `TableEmitter`: emit Condition table row (Feature_, Component_, Condition)
5. In `ModelValidator`: add SVC011

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServiceBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~ComponentResolverTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(core): add ServiceModel.ComponentCondition with SVC011 validation"
```

---

## TASK 4: FileEntry NeverOverwrite and Permanent

**Gap:** WiX File/@NeverOverwrite and File/@Permanent map to component attribute bits. FalkForge has no equivalent.

**Files to modify:**
- `src/FalkForge.Core/Models/FileEntryModel.cs` — add `bool NeverOverwrite` and `bool Permanent` (init-only, default false)
- `src/FalkForge.Core/Builders/FileSetBuilder.cs` — add `NeverOverwrite()` and `Permanent()` methods returning `this`
- `src/FalkForge.Compiler.Msi/ComponentResolver.cs` — when resolving file component attributes: if `NeverOverwrite`, set bit `0x100`; if `Permanent`, set bit `0x10`

**Tests to create:**
- `tests/FalkForge.Core.Tests/Builders/FileSetBuilderTests.cs` (directory exists but file does not — create)
- Extend `tests/FalkForge.Compiler.Msi.Tests/ComponentResolverTests.cs`

### Step 1: Write failing tests

```csharp
// FileSetBuilderTests:
// 1. NeverOverwrite_SetsFlag — model.NeverOverwrite is true
// 2. Permanent_SetsFlag — model.Permanent is true
// 3. DefaultFlags_AreFalse — both false by default

// ComponentResolverTests:
// 4. ResolveComponent_NeverOverwrite_SetsBit0x100
// 5. ResolveComponent_Permanent_SetsBit0x10
// 6. ResolveComponent_BothFlags_SetsBothBits
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~FileSetBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~ComponentResolverTests"
```

### Step 3: Implement

1. Add properties to `FileEntryModel`
2. Add fluent methods to `FileSetBuilder`, wire in `Build()`
3. In `ComponentResolver`: OR the attribute bits when building component attributes

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~FileSetBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj --filter "FullyQualifiedName~ComponentResolverTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(core): add FileEntry NeverOverwrite and Permanent flags"
```

---

## TASK 5: Service Permissions

**Gap:** WiX allows setting permissions (MsiLockPermissionsEx) on ServiceInstall entries. FalkForge PermissionModel only supports File/Registry/CreateFolder tables.

**Files to modify:**
- `src/FalkForge.Core/Builders/ServiceBuilder.cs` — add `Permission(Action<PermissionBuilder> configure)` that auto-sets `ForTable("ServiceInstall")` and `LockObject` to service name
- `src/FalkForge.Core/Models/ServiceModel.cs` — add `IReadOnlyList<PermissionModel> Permissions` (init-only)
- `src/FalkForge.Compiler.Msi/Tables/TableEmitter.cs` — in `EmitPermissions()`: ensure "ServiceInstall" is accepted as a valid table value
- `src/FalkForge.Core/Validation/ModelValidator.cs` — accept "ServiceInstall" as valid table in permission validation

**Tests to create:**
- `tests/FalkForge.Core.Tests/Builders/ServicePermissionTests.cs`

### Step 1: Write failing tests

```csharp
// Tests:
// 1. Permission_AddsPermissionToModel — builder.Permission(p => p.User("NT SERVICE\\svc").GenericRead()) adds to model
// 2. Permission_AutoSetsTable — table is "ServiceInstall"
// 3. Permission_AutoSetsLockObject — LockObject matches service name
// 4. Permission_MultiplePermissions — can add multiple permission entries
// 5. EmitPermissions_ServiceInstall_EmitsMsiLockPermissionsExRow
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServicePermissionTests"
```

### Step 3: Implement

1. Add `Permissions` collection to `ServiceModel`
2. Add `Permission(Action<PermissionBuilder>)` to `ServiceBuilder` — internally sets ForTable + LockObject
3. In `TableEmitter.EmitPermissions()`: add "ServiceInstall" to accepted table set
4. In `ModelValidator`: add "ServiceInstall" to valid permission table names

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~ServicePermissionTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(core): add service permissions via MsiLockPermissionsEx"
```

---

## TASK 6: Prerequisite PackageGroups (Largest Task)

**Gap:** WiX has PackageGroup for grouping reusable prerequisite packages. FalkForge has no equivalent abstraction for bundling common prerequisites.

**Files to create:**
- `src/FalkForge.Compiler.Bundle/PackageGroupModel.cs`
- `src/FalkForge.Compiler.Bundle/Builders/PackageGroupBuilder.cs`
- `src/FalkForge.Compiler.Bundle/Prerequisites/BuiltInPrerequisites.cs`

**Files to modify:**
- `src/FalkForge.Compiler.Bundle/Builders/ChainBuilder.cs` — add `PackageGroup(string id, Action<PackageGroupBuilder> configure)` and `PackageGroup(PackageGroupModel group)` overloads
- `src/FalkForge.Compiler.Bundle/BundleModel.cs` — add `IReadOnlyList<PackageGroupModel> PackageGroups`
- `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs` — flatten PackageGroups into chain during manifest generation

**Tests to create:**
- `tests/FalkForge.Compiler.Bundle.Tests/Builders/PackageGroupBuilderTests.cs`
- `tests/FalkForge.Compiler.Bundle.Tests/Builders/BuiltInPrerequisiteTests.cs`

### Step 1: Write failing tests

```csharp
// PackageGroupBuilderTests:
// 1. Id_SetsGroupId — group model has expected id
// 2. MsiPackage_AddsToGroup — package added to group's Packages list
// 3. ExePackage_AddsToGroup — exe package added
// 4. Build_ReturnsValidModel — full round-trip
// 5. ChainBuilder_PackageGroup_FlattensIntoChain — packages appear in chain items
// 6. ChainBuilder_MultipleGroups_PreservesOrder — ordering is maintained

// BuiltInPrerequisiteTests:
// 7. NetFx472_ReturnsValidGroup — has correct id, detection registry key, download URL
// 8. VCRedist14x64_ReturnsValidGroup — valid package group
// 9. OdbcDriver17_ReturnsValidGroup — valid package group
// 10. SqlExpress2017_ReturnsValidGroup — valid package group
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~PackageGroupBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~BuiltInPrerequisiteTests"
```

### Step 3: Implement

1. Create `PackageGroupModel` — `string Id`, `IReadOnlyList<BundlePackageModel> Packages`
2. Create `PackageGroupBuilder` — `Id(string)`, `MsiPackage(Action<BundlePackageBuilder>)`, `ExePackage(Action<BundlePackageBuilder>)`, `Build()`
3. Add `PackageGroup` overloads to `ChainBuilder`
4. Add `PackageGroups` to `BundleModel`
5. Create `BuiltInPrerequisites` with 4 static factory methods
6. In `ManifestGenerator.Generate()`: iterate `PackageGroups`, flatten each group's packages into the main chain before existing chain items

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~PackageGroupBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~BuiltInPrerequisiteTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(bundle): add PackageGroup model with built-in prerequisites"
```

---

## TASK 7: RegistrySearch Builder API

**Gap:** WiX RegistrySearch is used extensively for detection. FalkForge SearchConditionBuilder has no registry-specific helpers.

**Files to modify:**
- `src/FalkForge.Compiler.Bundle/Builders/SearchConditionBuilder.cs` — add `RegistryExists(RegistryRoot, string key, string? valueName)` and `RegistryValue(RegistryRoot, string key, string valueName, string comparison, string expectedValue)`
- `src/FalkForge.Engine/Detection/SearchConditionEvaluator.cs` (or similar) — handle `SearchConditionType.RegistryValue` using `IRegistry`

**Tests to extend:**
- `tests/FalkForge.Compiler.Bundle.Tests/Builders/SearchConditionBuilderTests.cs` (exists — extend)
- `tests/FalkForge.Engine.Tests/Detection/` — create `SearchConditionEvaluatorTests.cs` if not exists

### Step 1: Write failing tests

```csharp
// SearchConditionBuilderTests:
// 1. RegistryExists_SetsTypeAndPath — type is RegistryValue, path is "HKLM\Software\App"
// 2. RegistryExists_WithValueName_IncludesInPath — path includes value name
// 3. RegistryValue_SetsComparisonAndExpected — comparison and expected value stored
// 4. RegistryValue_AllRoots_MapCorrectly — HKLM, HKCU, HKCR, HKU

// SearchConditionEvaluatorTests:
// 5. Evaluate_RegistryExists_ReturnsTrueWhenKeyExists
// 6. Evaluate_RegistryExists_ReturnsFalseWhenKeyMissing
// 7. Evaluate_RegistryValue_MatchesExpectedValue
// 8. Evaluate_RegistryValue_FailsOnMismatch
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~SearchConditionBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FullyQualifiedName~SearchConditionEvaluatorTests"
```

### Step 3: Implement

1. Add `RegistryExists()` and `RegistryValue()` to `SearchConditionBuilder`
2. Map `RegistryRoot` enum to path prefix strings (HKLM, HKCU, HKCR, HKU)
3. In evaluator: parse path prefix back to root, use `IRegistry.GetValue()` for detection
4. Handle missing keys/values gracefully (return false for exists, fail comparison for value)

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~SearchConditionBuilderTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FullyQualifiedName~SearchConditionEvaluatorTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(bundle): add RegistrySearch builder API and evaluator support"
```

---

## TASK 8: Bundle Permanent Package

**Gap:** WiX Bundle supports Permanent="yes" on packages (survive uninstall). FalkForge has no equivalent.

**Files to modify:**
- `src/FalkForge.Compiler.Bundle/BundlePackageModel.cs` — add `bool Permanent` (init-only, default false)
- `src/FalkForge.Compiler.Bundle/Builders/BundlePackageBuilder.cs` — add `Permanent(bool permanent = true)` returning `this`
- `src/FalkForge.Engine.Protocol/Manifest/PackageInfo.cs` — add `bool Permanent`
- `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs` — emit `Permanent = package.Permanent`
- `src/FalkForge.Engine/Planning/Planner.cs` — when planning uninstall, skip packages where `Permanent == true`
- `src/FalkForge.Engine/Execution/PackageExecutor.cs` — respect Permanent flag
- `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` — add BDL026: Permanent requires ExePackage or MsiPackage type

**Tests to create:**
- `tests/FalkForge.Compiler.Bundle.Tests/Builders/BundlePackagePermanentTests.cs`
- Extend `tests/FalkForge.Engine.Tests/Planning/PlannerTests.cs`

### Step 1: Write failing tests

```csharp
// BundlePackagePermanentTests:
// 1. Permanent_SetsFlag — model.Permanent is true
// 2. Permanent_DefaultFalse — false by default
// 3. BDL026_PermanentMsuPackage_ProducesError — validation error for MSU
// 4. BDL026_PermanentMsiPackage_NoError — no error for MSI

// PlannerTests (extend):
// 5. PlanUninstall_PermanentPackage_SkippedInPlan
// 6. PlanInstall_PermanentPackage_IncludedInPlan — permanent only affects uninstall
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~BundlePackagePermanentTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FullyQualifiedName~PlannerTests"
```

### Step 3: Implement

1. Add `Permanent` to `BundlePackageModel` and builder
2. Add `Permanent` to `PackageInfo`
3. In `ManifestGenerator`: emit the flag
4. In `Planner`: check `Permanent` during uninstall planning — skip permanent packages
5. In `BundleValidator`: add BDL026

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~BundlePackagePermanentTests"
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FullyQualifiedName~PlannerTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(bundle): add Permanent package flag with BDL026 validation"
```

---

## TASK 9: EnableFeatureSelection + ManifestGenerator Fix

**Gap:** WiX MsiPackage/@EnableFeatureSelection allows per-feature install UI. Also, existing bug: ManifestGenerator does not emit DetectionMode or SearchConditions to PackageInfo.

**Files to modify:**
- `src/FalkForge.Compiler.Bundle/BundlePackageModel.cs` — add `bool EnableFeatureSelection` (init-only, default false)
- `src/FalkForge.Compiler.Bundle/Builders/BundlePackageBuilder.cs` — add `EnableFeatureSelection(bool enable = true)` returning `this`
- `src/FalkForge.Engine.Protocol/Manifest/PackageInfo.cs` — add `bool EnableFeatureSelection`
- `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs` — **FIX BUG**: add `DetectionMode`, `SearchConditions`, and `EnableFeatureSelection` to PackageInfo emission
- `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` — add BDL027: EnableFeatureSelection only valid for MsiPackage type

**Tests to create:**
- `tests/FalkForge.Compiler.Bundle.Tests/Compilation/ManifestGeneratorTests.cs` (create if not exists)

### Step 1: Write failing tests

```csharp
// ManifestGeneratorTests:
// 1. EnableFeatureSelection_SetsFlag — model flag is true
// 2. EnableFeatureSelection_DefaultFalse — false by default
// 3. BDL027_EnableFeatureSelection_ExePackage_ProducesError — validation error for non-MSI
// 4. BDL027_EnableFeatureSelection_MsiPackage_NoError — no error for MSI
// 5. Generate_EmitsDetectionMode — BUG FIX: PackageInfo.DetectionMode matches model
// 6. Generate_EmitsSearchConditions — BUG FIX: PackageInfo.SearchConditions matches model
// 7. Generate_EmitsEnableFeatureSelection — PackageInfo.EnableFeatureSelection matches model
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~ManifestGeneratorTests"
```

### Step 3: Implement

1. Add `EnableFeatureSelection` to `BundlePackageModel` and builder
2. Add `EnableFeatureSelection` to `PackageInfo`
3. **FIX BUG** in `ManifestGenerator.Generate()`: when building `PackageInfo`, add:
   - `DetectionMode = package.DetectionMode`
   - `SearchConditions = package.SearchConditions`
   - `EnableFeatureSelection = package.EnableFeatureSelection`
4. In `BundleValidator`: add BDL027

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj --filter "FullyQualifiedName~ManifestGeneratorTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(bundle): add EnableFeatureSelection, fix ManifestGenerator missing fields"
```

---

## TASK 10: ExeExecutor Variable Resolution

**Gap:** WiX Burn resolves `[VariableName]` references in ExePackage arguments at runtime. FalkForge ExeExecutor passes arguments verbatim.

**Files to modify:**
- `src/FalkForge.Engine/Execution/ExeExecutor.cs` — before passing arguments to ProcessRunner, resolve `[VariableName]` references using VariableStore
- Add private method `ResolveVariables(string input, VariableStore variables)` that replaces `[name]` with variable values

**Tests to extend:**
- `tests/FalkForge.Engine.Tests/Execution/ExeExecutorTests.cs` (exists — extend)

### Step 1: Write failing tests

```csharp
// ExeExecutorTests (extend):
// 1. ResolveVariables_SingleVariable_Replaced — "[InstallDir]" → "C:\App"
// 2. ResolveVariables_MultipleVariables_AllReplaced — "[Dir]\[App]" → "C:\MyApp\app.exe"
// 3. ResolveVariables_MissingVariable_LeftUnreplaced — "[Unknown]" stays "[Unknown]"
// 4. ResolveVariables_NoVariables_PassedThrough — "plain text" unchanged
// 5. ResolveVariables_NestedBrackets_HandledCorrectly — "[[literal]]" edge case
// 6. Execute_WithVariableArgs_ResolvesBeforeExec — integration: variable-containing args resolved before process start
```

### Step 2: Run tests — verify failure

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FullyQualifiedName~ExeExecutorTests"
```

### Step 3: Implement

1. Add `ResolveVariables(string input, VariableStore variables)` to `ExeExecutor`:
   - Regex: `\[([A-Za-z_][A-Za-z0-9_.]*)\]`
   - For each match, try `variables.GetValue(name)` — if found, replace; if not, leave as-is and log warning
2. Call `ResolveVariables()` on the arguments string before passing to `ProcessRunner.Start()`
3. Handle null/empty arguments (no-op)

### Step 4: Run tests — verify pass

```bash
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FullyQualifiedName~ExeExecutorTests"
```

### Step 5: Commit

```bash
git -C D:/Git/FalkInstaller/.worktrees/gap-fixes add -A && git -C D:/Git/FalkInstaller/.worktrees/gap-fixes commit -m "feat(engine): resolve [Variable] references in ExeExecutor arguments"
```

---

## Execution Order

Tasks are ordered by dependency and complexity:

| Order | Task | Complexity | Dependencies |
|-------|------|------------|--------------|
| 1 | ServiceModel.Arguments | Low | None |
| 2 | Service AccountProperty | Low | Task 1 (same files) |
| 3 | Service ComponentCondition | Medium | Task 2 (same files) |
| 4 | FileEntry NeverOverwrite/Permanent | Low | None |
| 5 | Service Permissions | Medium | Tasks 1-3 (same model) |
| 6 | Prerequisite PackageGroups | High | None |
| 7 | RegistrySearch Builder API | Medium | None |
| 8 | Bundle Permanent Package | Medium | None |
| 9 | EnableFeatureSelection + ManifestGenerator fix | Medium | Task 8 (same files) |
| 10 | ExeExecutor Variable Resolution | Medium | None |

Tasks 1-3 and 5 share ServiceModel/ServiceBuilder — do them in sequence.
Tasks 4, 6, 7, 8, 10 are independent and could theoretically be parallelized.
Task 9 should follow Task 8 since both touch BundlePackageModel and ManifestGenerator.

## Final Verification

After all 10 tasks:

```bash
dotnet build D:/Git/FalkInstaller/.worktrees/gap-fixes/FalkForge.sln
dotnet test D:/Git/FalkInstaller/.worktrees/gap-fixes/FalkForge.sln
```

Both must complete with zero warnings and zero test failures.
