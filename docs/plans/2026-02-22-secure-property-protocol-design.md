# Secure Property Protocol Design

## Overview

Wire `SetProperty` / `SetSecureProperty` through the named pipe protocol to the MSI installer engine, replacing `msiexec.exe` process spawning with `MsiInstallProduct` P/Invoke for secure in-process execution. Secure properties never appear on a process command line.

## Problem Statement

The current engine spawns `msiexec.exe` via `Process.Start`, passing MSI properties on the command line. This exposes sensitive values (database connection strings, service credentials, license keys) in:

- Process command-line arguments (visible via Task Manager, `wmic process`, ETW)
- WMI event logs
- Security audit logs (Process Creation events, Sysmon)

Additionally, `SetProperty` and `SetSecureProperty` on `IInstallerEngine` currently throw `NotSupportedException` — the UI-to-engine property pipeline is unimplemented.

## Design Decisions

### 1. Two New Protocol Messages

| Message | Code | Payload | Purpose |
|---------|------|---------|---------|
| `SetPropertyMessage` | `0x0208` | `string Name` + `string Value` | Regular properties (INSTALLFOLDER, ALLUSERS, etc.) |
| `SetSecurePropertyMessage` | `0x0209` | `string Name` + `int Length` + `byte[] Value` | Sensitive properties (passwords, connection strings) |

**Why two messages?** Regular properties have relaxed lifecycle — they can be logged, stored as strings, included in error reports. Secure properties require pinned memory, zeroing on dispose, and must never be serialized as cleartext strings outside the pipe.

**Why `0x0208` / `0x0209`?** These follow the existing message type numbering in the `MessageType` enum. The `0x02xx` range is used for request messages from UI to engine.

### 2. Regular Properties: VariableStore.Set()

Regular properties flow through the standard `VariableStore.Set(name, value)` path. They are stored as plain strings, logged normally, and assembled into the MSI command-line property string for `MsiInstallProduct`.

### 3. Secure Properties: VariableStore.SetSecret() as SecureVariable

Secure properties are stored as `SecureVariable` — a `sealed class` wrapping a GC-pinned `byte[]` that is zeroed on `Dispose()`:

```csharp
internal sealed class SecureVariable : IDisposable
{
    private readonly GCHandle _handle;
    private readonly byte[] _data;
    private bool _disposed;

    public SecureVariable(ReadOnlySpan<byte> value)
    {
        _data = value.ToArray();
        _handle = GCHandle.Alloc(_data, GCHandleType.Pinned);
    }

    public ReadOnlySpan<byte> Value => _disposed
        ? throw new ObjectDisposedException(nameof(SecureVariable))
        : _data;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_data);
        _handle.Free();
    }
}
```

`VariableStore.SetSecret(name, bytes)` creates a `SecureVariable` and stores it in a separate `ConcurrentDictionary<string, SecureVariable>`. Previous values for the same key are disposed before replacement.

### 4. MsiInstallProduct P/Invoke Replaces msiexec.exe

The core security improvement: instead of spawning `msiexec.exe` with properties on the command line, call `MsiInstallProduct` directly via P/Invoke. The property string is built in-process memory, passed to the MSI API, and zeroed immediately after.

```csharp
// Platform.Windows — shared by Engine and Elevation
[LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial uint MsiInstallProductW(string szPackagePath, string? szCommandLine);

[LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial uint MsiConfigureProductW(string szProductCode, int iInstallLevel, int iInstallState);

[LibraryImport("msi.dll")]
internal static partial int MsiSetInternalUI(int dwUILevel, nint phWnd);
```

**Why MsiInstallProduct over MsiInstallProductEx?** `MsiInstallProduct` is simpler, available since Windows Installer 1.0, and sufficient for our needs. The `Ex` variant adds transaction support we don't currently use.

**Why MsiConfigureProduct for uninstalls?** `MsiConfigureProduct(productCode, INSTALLLEVEL_DEFAULT, INSTALLSTATE_ABSENT)` is the canonical P/Invoke approach for uninstall. It avoids constructing the `/x {GUID}` command line entirely.

### 5. P/Invoke Location: Platform.Windows

The P/Invoke declarations live in `src/FalkForge.Platform.Windows/` because:

- Both `Engine` and `Engine.Elevation` reference `Platform.Windows`
- The compiler (`Compiler.Msi`) already has its own `NativeMethods.Msi.cs` for MSI database operations
- Engine-side MSI execution is a platform concern, not a compiler concern

### 6. EngineHost Phase Gating

`EngineHost.HandleUiMessageAsync()` accepts `SetPropertyMessage` and `SetSecurePropertyMessage` only during `Initializing`, `Detecting`, and `Planning` phases. Setting properties during `Applying` is rejected with an error message — the plan is already locked.

### 7. Planner Property Propagation

`VariableStore` maintains a `HashSet<string> _userProperties` tracking names set via UI messages (as opposed to built-in variables). When the `Planner` builds `PlanAction` instances for MSI packages, it:

1. Iterates `_userProperties` to find user-set regular properties
2. Builds the property string: `PROP1=value1 PROP2=value2`
3. For secure properties: resolves `SecureVariable` at execution time via `VariableStore.GetSecret(name)`, decodes to string, appends to the property string, and zeroes the temporary string after `MsiInstallProduct` returns

The property string is stored in `PlanAction.Properties` as a `Dictionary<string, string>` for regular properties. Secure property names are stored in `PlanAction.SecretPropertyNames` as a `HashSet<string>` — the actual values are resolved from `VariableStore` at execution time to minimize the window of exposure.

### 8. Elevation Protocol

When the engine needs elevated MSI execution:

1. `MsiExecutor` builds the property string (including resolved secrets)
2. Sends `ElevateExecuteMessage` over pipe B with the property string
3. `MsiInstallCommand` (elevated) calls `MsiInstallProduct` P/Invoke
4. Property string is zeroed after the call
5. Result flows back as `ElevateResultMessage`

The secret values cross pipe B, which is HMAC-SHA256 authenticated. They never appear in a process command line.

## Data Flow

```
┌─────────────┐    ┌──────────────┐    ┌────────────────┐    ┌──────────────┐
│  UI Page    │    │ EngineClient │    │   EngineHost   │    │  Elevated    │
│  (WPF)     │    │  (Ui.csproj) │    │ (Engine.csproj)│    │  Process     │
└──────┬──────┘    └──────┬───────┘    └───────┬────────┘    └──────┬───────┘
       │                  │                    │                    │
       │ SetProperty()    │                    │                    │
       │ SetSecureProperty()                   │                    │
       ├─────────────────►│                    │                    │
       │                  │ SetPropertyMessage │                    │
       │                  │ SetSecurePropertyMessage                │
       │                  ├───── pipe A ──────►│                    │
       │                  │  (HMAC-SHA256)     │                    │
       │                  │                    │ VariableStore      │
       │                  │                    │   .Set(name, val)  │
       │                  │                    │   .SetSecret(name, │
       │                  │                    │     bytes)         │
       │                  │                    │                    │
       │                  │                    │ Planner            │
       │                  │                    │   Reads _userProperties
       │                  │                    │   Builds PlanAction│
       │                  │                    │     .Properties    │
       │                  │                    │     .SecretPropertyNames
       │                  │                    │                    │
       │                  │                    │ MsiExecutor        │
       │                  │                    │   Resolves secrets │
       │                  │                    │   Builds cmdline   │
       │                  │                    │                    │
       │                  │                    │ [Non-elevated]     │
       │                  │                    │ MsiInstallProduct()│
       │                  │                    │ Zero cmdline       │
       │                  │                    │                    │
       │                  │                    │ [Elevated]         │
       │                  │                    ├───── pipe B ──────►│
       │                  │                    │  (HMAC-SHA256)     │
       │                  │                    │                    │ MsiInstallCommand
       │                  │                    │                    │ MsiInstallProduct()
       │                  │                    │                    │ Zero cmdline
       │                  │                    │◄───── pipe B ─────┤
       │                  │                    │  ElevateResult     │
       │                  │                    │                    │
```

## Security Model

### Threat Model

| Threat | Mitigation |
|--------|------------|
| Command-line sniffing (Task Manager, WMI, ETW) | Eliminated — no process spawning for MSI operations |
| Process memory dump | SecureVariable: pinned + zeroed on dispose; property string zeroed after MsiInstallProduct |
| Pipe eavesdropping | HMAC-SHA256 handshake on both pipe A and pipe B |
| Pipe injection | Named pipe security descriptors + parent PID verification (Elevation) |
| Memory not zeroed on crash | GC-pinned array ensures no relocation; OS reclaims memory on process exit |
| Secrets in logs | VariableStore.GetSecret() returns raw bytes, never logged; regular logging skips `_secretProperties` keys |

### Secret Lifecycle

```
1. UI calls SetSecureProperty(name, SensitiveBytes)
2. EngineClient copies Span to byte[], creates SetSecurePropertyMessage, zeros copy
3. MessageSerializer writes: [name length][name UTF-16][value length][raw bytes]
4. Pipe transport: HMAC-SHA256 authenticated, sent over named pipe
5. EngineHost receives, calls VariableStore.SetSecret(name, bytes)
6. VariableStore creates SecureVariable (pinned byte[])
7. (Later) MsiExecutor resolves secret: decodes bytes to char[], builds property string
8. MsiInstallProduct(path, propertyString) — in-process call
9. Property string zeroed (Array.Clear on backing char[])
10. SecureVariable.Dispose() — zeros pinned byte[], frees GCHandle
```

### What This Does NOT Protect Against

- Kernel-mode memory inspection (requires admin/kernel access — outside threat model)
- Debugger attached to the engine process (requires same-user or admin — outside threat model)
- MSI custom actions that log properties (responsibility of the MSI package author)
- The MSI database itself storing property values in `Property` table (by design)

## Constants

```csharp
// msi.dll constants
internal const int INSTALLLEVEL_DEFAULT = 0;
internal const int INSTALLSTATE_ABSENT = 2;
internal const int INSTALLUILEVEL_NONE = 2;

// MsiInstallProduct return codes
internal const uint ERROR_SUCCESS = 0;
internal const uint ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
internal const uint ERROR_INSTALL_USEREXIT = 1602;
internal const uint ERROR_INSTALL_FAILURE = 1603;
```

## Compatibility

- `IProcessRunner` is retained for non-MSI executors (MSU, MSP, EXE bundles)
- Existing tests mock `IProcessRunner` — MSI-specific tests will need updating to mock the P/Invoke layer (via an `IMsiApi` abstraction or a static function pointer)
- `MsiExecutor` gains an `IMsiApi` dependency for testability:

```csharp
internal interface IMsiApi
{
    uint InstallProduct(string packagePath, string? commandLine);
    uint ConfigureProduct(string productCode, int installLevel, int installState);
    int SetInternalUI(int uiLevel, nint window);
}
```

Production implementation delegates to P/Invoke. Test implementation returns configurable results.

## Affected Projects

| Project | Changes |
|---------|---------|
| `FalkForge.Platform.Windows` | New P/Invoke declarations for MsiInstallProduct, MsiConfigureProduct, MsiSetInternalUI |
| `FalkForge.Engine.Protocol` | Two new message types, serialization/deserialization |
| `FalkForge.Ui` | EngineClient: replace NotSupportedException stubs |
| `FalkForge.Engine` | EngineHost dispatch, VariableStore (SetSecret, SecureVariable, _userProperties), Planner (property propagation), MsiExecutor (P/Invoke) |
| `FalkForge.Engine.Elevation` | MsiInstallCommand + MsiUninstallCommand (P/Invoke) |
| `FalkForge.Core.Tests` | — |
| `FalkForge.Engine.Tests` | VariableStore, Planner, MsiExecutor tests |
| `FalkForge.Engine.Protocol.Tests` | Roundtrip serialization tests |
| `FalkForge.Engine.Elevation.Tests` | Updated command tests |
| `FalkForge.Ui.Tests` | EngineClient property tests |

## Open Questions

1. **String zeroing in .NET:** `string` is immutable in .NET — we cannot zero it after passing to `MsiInstallProduct`. Should we use `char[]` and `fixed` to pass to the P/Invoke, or accept that the managed string lives until GC collects it? **Decision: Use `char[]` with `fixed` pointer and a custom marshaller to avoid the immutable string copy.**

2. **MsiInstallProduct threading:** MSI API is apartment-threaded (STA). The engine runs on a background thread. Do we need `MarshalByRefObject` or `SynchronizationContext` dispatch? **Decision: Call from a dedicated STA thread, same pattern as WPF dispatch. The engine already has thread affinity for pipe I/O.**

3. **Retry on ERROR_INSTALL_ALREADY_RUNNING (1618)?** **Decision: Yes, with exponential backoff up to 3 retries, matching the existing MsiExecutor retry logic for transient failures.**
