# Magic String & Magic Number Cleanup Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace all magic strings and magic numbers in the FalkForge codebase with enums, constants, and typed APIs.

**Architecture:** Seven independent refactoring tasks, each introducing a new enum or constants class and updating all call sites. All tasks are backward-compatible — existing string-based APIs get overloads, not replacements, where they form public API.

**Tech Stack:** C#, enums, [Flags] enums, const strings, static classes.

---

### Task 1: ShellVerb Enum — Replace `.Verb("open")` magic strings

**Scope:** Create `ShellVerb` enum in Core, add `Verb(ShellVerb)` overload to `FileAssociationBuilder`, update 5 demos and 1 tutorial.

**Files:**
- Create: `src/FalkForge.Core/Models/ShellVerb.cs`
- Modify: `src/FalkForge.Core/Builders/VerbBuilder.cs` — add ctor overload taking `ShellVerb`
- Modify: `src/FalkForge.Core/Builders/FileAssociationBuilder.cs` — add `Verb(ShellVerb, ...)` overload
- Modify: `demo/04-dev-toolkit/Program.cs` — replace `"open"` with `ShellVerb.Open`
- Modify: `demo/05-enterprise-suite/Program.cs` — replace `"open"` with `ShellVerb.Open`
- Modify: `demo/19-file-associations/Program.cs` — replace `"open"` with `ShellVerb.Open`
- Modify: `docs/tutorials/msi-basics.html` — update code example
- Test: `tests/FalkForge.Core.Tests/Builders/VerbBuilderTests.cs` — add test for ShellVerb overload

**ShellVerb enum:**
```csharp
namespace FalkForge.Models;

public enum ShellVerb
{
    Open,
    Edit,
    Print,
    PrintTo,
    Explore,
    RunAs,
    Play
}
```

**VerbBuilder changes:** Add constructor `VerbBuilder(ShellVerb verb)` that converts to lowercase string (`verb.ToString().ToLowerInvariant()`). Keep existing `VerbBuilder(string verb)` constructor.

**FileAssociationBuilder changes:** Add `Verb(ShellVerb verb, string? argument = null, Action<VerbBuilder>? configure = null)` overload that delegates to the existing string-based method.

**Build, test, update demos, commit.**

```
refactor(core): add ShellVerb enum to replace magic verb strings
```

---

### Task 2: RegistryRoot Enum for IRegistry — Replace `"HKLM"` magic strings

**Scope:** Change `IRegistry` interface from `string rootKey` to `RegistryRoot` enum parameter. Update all ~20 call sites across Platform, Engine, Elevation, Extensions.

**IMPORTANT:** A `RegistryRoot` or `RegistryHive` enum may already exist in Core (check `src/FalkForge.Core/Models/` for registry-related enums). If it exists, use it. If not, create one in Platform (since IRegistry lives there).

**Files:**
- Create or reuse: `src/FalkForge.Platform/RegistryRootKey.cs` (if no existing enum fits)
- Modify: `src/FalkForge.Platform/IRegistry.cs` — change all 6 methods from `string rootKey` to enum
- Modify: `src/FalkForge.Platform.Windows/WindowsRegistry.cs` — update `GetRootKey` to use enum switch
- Modify: All callers in Engine (~12 files), Elevation (~1 file), Extensions (~2 files)

**Enum (if new):**
```csharp
namespace FalkForge.Platform;

public enum RegistryRootKey
{
    LocalMachine,   // HKLM
    CurrentUser,    // HKCU
    ClassesRoot,    // HKCR
    Users           // HKU
}
```

**WindowsRegistry.GetRootKey changes:**
```csharp
private static RegistryKey GetRootKey(RegistryRootKey root) => root switch
{
    RegistryRootKey.LocalMachine => Registry.LocalMachine,
    RegistryRootKey.CurrentUser => Registry.CurrentUser,
    RegistryRootKey.ClassesRoot => Registry.ClassesRoot,
    RegistryRootKey.Users => Registry.Users,
    _ => throw new ArgumentOutOfRangeException(nameof(root))
};
```

**Caller updates:** Replace `"HKLM"` with `RegistryRootKey.LocalMachine`, `"HKCU"` with `RegistryRootKey.CurrentUser`, etc. across:
- `src/FalkForge.Engine/Variables/BuiltInVariables.cs`
- `src/FalkForge.Engine/Variables/VariablePersistence.cs`
- `src/FalkForge.Engine/Variables/FeaturePersistence.cs`
- `src/FalkForge.Engine/Detection/DependencyDetector.cs`
- `src/FalkForge.Engine/Detection/RelatedBundleDetector.cs`
- `src/FalkForge.Engine/Detection/MsiDetector.cs`
- `src/FalkForge.Engine.Elevation/Commands/RegistryWriteCommand.cs`
- `src/FalkForge.Extensions.DotNet/DotNetDetector.cs`
- Any other callers found via compiler errors after the interface change

**Build, run all tests, commit.**

```
refactor(platform): replace string rootKey with RegistryRootKey enum in IRegistry
```

---

### Task 3: MsiControlAttributes and MsiDialogAttributes — Replace magic numbers

**Scope:** Create two `[Flags]` enums for MSI control and dialog attribute bitmasks. Replace all numeric literals in templates.

**Files:**
- Create: `src/FalkForge.Compiler.Msi/UI/MsiControlAttributes.cs`
- Create: `src/FalkForge.Compiler.Msi/UI/MsiDialogAttributes.cs`
- Modify: `src/FalkForge.Compiler.Msi/UI/MsiControlModel.cs` — change `Attributes` type to `MsiControlAttributes`
- Modify: `src/FalkForge.Compiler.Msi/UI/MsiDialogModel.cs` — change `Attributes` type to `MsiDialogAttributes`
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs` — replace all numeric literals
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/MinimalDialogTemplate.cs` — same
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/AdvancedDialogTemplate.cs` — same
- Modify: `src/FalkForge.Compiler.Msi/UI/DialogEmitter.cs` — cast enum to int when writing to MSI table

**MsiControlAttributes:**
```csharp
namespace FalkForge.Compiler.Msi.UI;

[Flags]
internal enum MsiControlAttributes
{
    Visible         = 0x00000001,
    Enabled         = 0x00000002,
    Sunken          = 0x00000004,
    // ... standard MSI control attribute bits
    Transparent     = 0x00010000,
    NoPrefix        = 0x00020000,
    Progress95      = 0x00010000, // for ProgressBar type
    // Composed constants for common combinations:
}
```

**IMPORTANT:** The subagent must look up the exact MSI SDK bit definitions. Key values to map:
- `3` = Visible | Enabled
- `1` = Visible
- `5` = Visible | Minimize (dialog only)
- `7` = Visible | Enabled | Sunken
- `39` = Visible | Modal | Minimize (dialog)
- `196611` = Visible | Enabled | Transparent | NoPrefix (0x30003)
- `65539` = Visible | Enabled | Progress95 (0x10003)
- `393223` = Visible | Enabled | Sunken | RemovableMedia | FixedMedia | RemoteMedia | CDROMMedia (0x60007)

The DialogEmitter must cast the enum to `(int)` when writing to MSI tables since MSI stores these as integers.

**Build, run all tests (especially E2E demo tests), commit.**

```
refactor(msi): replace magic attribute numbers with MsiControlAttributes/MsiDialogAttributes enums
```

---

### Task 4: MsiControlType, MsiControlEvent, MsiConditionAction — Replace string-typed enums

**Scope:** Create three enums/static-constant classes for MSI control types, events, and condition actions. Update all UI models and templates.

**Files:**
- Create: `src/FalkForge.Compiler.Msi/UI/MsiControlType.cs`
- Create: `src/FalkForge.Compiler.Msi/UI/MsiControlEvent.cs`
- Create: `src/FalkForge.Compiler.Msi/UI/MsiConditionAction.cs`
- Modify: `src/FalkForge.Compiler.Msi/UI/MsiControlModel.cs` — `Type` property from `string` to enum
- Modify: `src/FalkForge.Compiler.Msi/UI/MsiControlEventModel.cs` — `Event` property from `string` to enum
- Modify: `src/FalkForge.Compiler.Msi/UI/MsiControlConditionModel.cs` — `Action` property from `string` to enum
- Modify: All template files — use enum values
- Modify: `src/FalkForge.Compiler.Msi/UI/DialogEmitter.cs` — convert enums to strings when writing to MSI tables

**MsiControlType enum (internal):**
```csharp
internal enum MsiControlType
{
    Text, PushButton, Line, CheckBox, ScrollableText, PathEdit,
    SelectionTree, VolumeCostList, ProgressBar, Bitmap, RadioButtonGroup,
    ComboBox, Edit, ListBox, DirectoryCombo, DirectoryList,
    VolumeCostList, MaskedEdit, Icon, GroupBox
}
```

**MsiControlEvent enum (internal):**
```csharp
internal enum MsiControlEvent
{
    NewDialog, SpawnDialog, EndDialog, SetProperty, DoAction,
    AddLocal, AddSource, Remove, Reset, SelectionBrowse
}
```

**MsiConditionAction enum (internal):**
```csharp
internal enum MsiConditionAction
{
    Disable, Enable, Hide, Show, Default
}
```

The DialogEmitter converts to string via `.ToString()` or a mapping method when writing to MSI tables.

**Build, run all tests, commit.**

```
refactor(msi): replace string control type/event/action with enums
```

---

### Task 5: BuiltInVariableNames Constants — Eliminate duplication

**Scope:** Create a shared constants class for the 32 built-in variable names. Reference from both EngineHost blocklist and BuiltInVariables.Populate().

**Files:**
- Create: `src/FalkForge.Engine/Variables/BuiltInVariableNames.cs`
- Modify: `src/FalkForge.Engine/EngineHost.cs` — replace inline HashSet with `BuiltInVariableNames.All`
- Modify: `src/FalkForge.Engine/Variables/BuiltInVariables.cs` — reference constants for variable names

**BuiltInVariableNames:**
```csharp
namespace FalkForge.Engine.Variables;

internal static class BuiltInVariableNames
{
    public const string VersionNT = "VersionNT";
    public const string VersionNTMajor = "VersionNTMajor";
    public const string VersionNTMinor = "VersionNTMinor";
    // ... all 32 names as const string fields

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        VersionNT, VersionNTMajor, VersionNTMinor, // ...
    };
}
```

**EngineHost change:** Replace the inline `new HashSet<string>` with `BuiltInVariableNames.All`.

**BuiltInVariables change:** Replace inline string literals like `store.Set("VersionNT", ...)` with `store.Set(BuiltInVariableNames.VersionNT, ...)`.

**Build, run Engine tests, commit.**

```
refactor(engine): extract BuiltInVariableNames constants from duplicated string literals
```

---

### Task 6: FALKBUNDLE Magic Constant — Consolidate 3 definitions

**Scope:** Move the FALKBUNDLE magic bytes to a single shared location and reference from all 3 files.

**Files:**
- Modify: `src/FalkForge.Engine.Protocol/Bundle/BundleReader.cs` — keep the canonical definition here (Engine.Protocol is referenced by both Compiler.Bundle and Decompiler)
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/PayloadEmbedder.cs` — reference `BundleReader.BundleMagic`
- Modify: `src/FalkForge.Decompiler/BundleAccess.cs` — reference `BundleReader.BundleMagic`

`BundleReader` already exposes `public static ReadOnlySpan<byte> BundleMagic`. The other two files just need to stop defining their own copies and use `BundleReader.BundleMagic` instead.

**Check dependency graph first:** Compiler.Bundle and Decompiler both need to reference Engine.Protocol. Verify these project references already exist (they likely do per CLAUDE.md dependency graph). If not, the subagent needs to add them OR create a shared constants class in Core instead.

**Build, run all tests, commit.**

```
refactor: consolidate FALKBUNDLE magic constant to single definition
```

---

### Task 7: Dialog Name Constants — Centralize scattered strings

**Scope:** Create a static constants class for MSI dialog names used across templates.

**Files:**
- Create: `src/FalkForge.Compiler.Msi/UI/Templates/DialogNames.cs`
- Modify: All 6 template files — replace string literals with constants

**DialogNames:**
```csharp
namespace FalkForge.Compiler.Msi.UI.Templates;

internal static class DialogNames
{
    public const string Welcome = "WelcomeDlg";
    public const string LicenseAgreement = "LicenseAgreementDlg";
    public const string InstallDir = "InstallDirDlg";
    public const string CustomizeDlg = "CustomizeDlg";
    public const string SetupType = "SetupTypeDlg";
    public const string InstallScope = "InstallScopeDlg";
    public const string Progress = "ProgressDlg";
    public const string Exit = "ExitDlg";
    public const string Cancel = "CancelDlg";
    public const string Browse = "BrowseDlg";
}
```

Replace all `"WelcomeDlg"` occurrences with `DialogNames.Welcome`, etc.

**Build, run E2E tests, commit.**

```
refactor(msi): centralize dialog name strings into DialogNames constants
```

---

### Task 8: Final Verification

**Step 1:** Build full solution: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
**Step 2:** Run full test suite: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx -v minimal`
**Step 3:** Run E2E tests: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Integration.Tests/ --filter DemoEndToEnd --no-build -v minimal`
**Step 4:** Verify no remaining magic strings in demos: grep for `\.Verb\("` in demo/ to confirm all use ShellVerb enum

```
refactor: complete magic string and number cleanup
```
