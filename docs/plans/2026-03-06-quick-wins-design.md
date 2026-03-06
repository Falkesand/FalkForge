# Quick Wins Design: PowerShell CA, COM Registration, Driver Installation

## Overview

Three independent features following the Model → Builder → Table Emission pattern.

## 1. PowerShell Custom Actions (Extensions.Util)

**Model:** `PowerShellActionModel` — Id, ScriptContent (inline or read from file), Is64Bit flag, CA type flags, Condition.

**Builder:** Two new methods on `CustomActionBuilder`:
- `PowerShellScript(string script)` — inline script, embedded in Binary table
- `PowerShellFile(string path)` — reads .ps1 at build time, embeds content

**Compilation:** Script stored in Binary table as `PS_{ActionId}`. CA emitted as `ExeInDir` (type 34) targeting `[SystemFolder]` with command: `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "& { [script content from property] }"`. For longer scripts, a SetProperty CA writes the script path, then ExeInDir executes it.

## 2. COM Registration (Core + Compiler.Msi)

**Models:**
- `ComClassModel` — ClassId (GUID), ServerType (InprocServer32/LocalServer32), ProgId, Description, ThreadingModel, AppId
- `ComTypeLibModel` — TypeLibId (GUID), FilePath, Version, Language (LCID), Description
- Enums: `ComServerType`, `ComThreadingModel`

**Builders:** `ComClassBuilder`, `ComTypeLibBuilder` — fluent API accessible from file/component context.

**Compilation:** Emits MSI `Class` table (CLSID, Context, Component_, ProgId_, Description), `TypeLib` table (LibID, Language, Component_, Version, Description), and links `ProgId` table Class_ column.

## 3. Driver Installation (Extensions.Driver — new project)

**Model:** `DriverModel` — Id, InfFilePath, ForceInstall flag, Condition.

**Builder:** `DriverBuilder` — fluent API via new `DriverExtension`.

**Compilation:** Emits deferred elevated CA calling `pnputil /install-driver "[INSTALLDIR]path.inf" /subdirs [/force]`. Uninstall CA reads stored OEM name from registry. No rollback in v1.

## Testing

~13 tests total: 4 PowerShell, 6 COM, 3 Driver.
