# Secure Password Handling for Custom UI Framework

**Date:** 2026-02-20
**Status:** Approved

## Problem

MAS demo pages store passwords as `string` properties with data binding. Strings are immutable in .NET — they can't be zeroed, persist until GC, and are visible in memory dumps. WPF's `PasswordBox` deliberately doesn't support binding for this reason.

Three MAS pages affected:
- `DatabaseConnectionSettingsPage._password` (SQL credentials)
- `MultiServerAdvancedSettingsPage._servicePassword` (service account)
- `MultiServerExAdvancedSettingsPage._servicePassword` (service account)

## Design Decisions

1. **Framework-level** — secure password handling built into InstallerPage base class
2. **Attached property** — declarative XAML `PasswordBridge.Key` for PasswordBox registration
3. **SensitiveBytes** — IDisposable `readonly struct` wrapping `byte[]` with automatic zeroing
4. **DPAPI encryption** — SharedState encrypts sensitive values via Windows DPAPI (CurrentUser scope)
5. **Project split** — SensitiveBytes + ISensitiveDataProtector in Ui.Abstractions, DPAPI impl in Ui
6. **Bundle-to-MSI transport** — deferred to separate design (secure property passing via Engine IPC)

## Components

### SensitiveBytes (`Ui.Abstractions/SensitiveBytes.cs`)

Readonly struct wrapping `byte[]`. Zeros memory on `Dispose()` via `CryptographicOperations.ZeroMemory`. Always used via `using` pattern.

```csharp
public readonly struct SensitiveBytes : IDisposable
{
    private readonly byte[] _data;
    public SensitiveBytes(byte[] data) => _data = data;
    public ReadOnlySpan<byte> Span => _data;
    public int Length => _data?.Length ?? 0;
    public bool IsEmpty => _data is null or { Length: 0 };
    public void Dispose()
    {
        if (_data is not null)
            CryptographicOperations.ZeroMemory(_data);
    }
}
```

### ISensitiveDataProtector (`Ui.Abstractions/ISensitiveDataProtector.cs`)

Abstraction for encrypt/decrypt. Keeps Ui.Abstractions cross-platform.

```csharp
public interface ISensitiveDataProtector
{
    byte[] Protect(byte[] plainData);
    byte[] Unprotect(byte[] protectedData);
}
```

### InstallerState Changes (`Ui.Abstractions/InstallerState.cs`)

- Separate `ConcurrentDictionary<string, byte[]>` for sensitive entries (stores encrypted bytes)
- `ISensitiveDataProtector` injected via constructor (optional, defaults to no-op for testing)
- `SetSensitive(string key, ReadOnlySpan<byte> data)` — copies, encrypts, stores. Zeros old value on overwrite.
- `GetSensitive(string key)` — decrypts, returns `SensitiveBytes` (caller owns lifecycle via using)
- Implements `IDisposable` — zeros all sensitive entries on cleanup

### DpapiDataProtector (`Ui/DpapiDataProtector.cs`)

Windows DPAPI implementation using `ProtectedData.Protect/Unprotect` with `DataProtectionScope.CurrentUser`. No admin rights required. Available on all Windows versions.

```csharp
public sealed class DpapiDataProtector : ISensitiveDataProtector
{
    public byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, entropy: null, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] data)
        => ProtectedData.Unprotect(data, entropy: null, DataProtectionScope.CurrentUser);
}
```

### PasswordBridge (`Ui/PasswordBridge.cs`)

WPF attached property. XAML usage: `<PasswordBox ui:PasswordBridge.Key="SqlPassword" />`. On key set, subscribes to DataContextChanged. When DataContext is InstallerPage, registers the PasswordBox. On unload, unregisters.

### InstallerPage Changes (`Ui/InstallerPage.cs`)

```csharp
private readonly Dictionary<string, PasswordBox> _passwordBoxes = new();

internal void RegisterPasswordBox(string key, PasswordBox box)
    => _passwordBoxes[key] = box;

internal void UnregisterPasswordBox(string key)
    => _passwordBoxes.Remove(key);

protected SensitiveBytes GetPassword(string key)
{
    if (!_passwordBoxes.TryGetValue(key, out var box))
        return default;
    return new SensitiveBytes(Encoding.UTF8.GetBytes(box.Password));
}
```

## MAS Demo Migration

Remove `string` password properties. Use `PasswordBridge.Key` in XAML. Read via `GetPassword()` only at point of use.

**Before:**
```csharp
private string _password = string.Empty;
public string Password { get => _password; set => SetField(ref _password, value); }
```

**After (page):**
```csharp
public override PageResult OnNext()
{
    using var pw = GetPassword("SqlPassword");
    SharedState.SetSensitive("DbPassword", pw.Span);
    return PageResult.Next;
}
```

**After (XAML):**
```xml
<PasswordBox ui:PasswordBridge.Key="SqlPassword" />
```

## Affected Pages

| Page | Old Property | PasswordBridge Key |
|------|-------------|-------------------|
| DatabaseConnectionSettingsPage | `_password` | `DbPassword` |
| MultiServerAdvancedSettingsPage | `_servicePassword` | `ServicePassword` |
| MultiServerExAdvancedSettingsPage | `_servicePassword` | `ServicePassword` |

## Testing Strategy

- `SensitiveBytes`: zeroing on dispose, empty/default handling
- `InstallerState.SetSensitive/GetSensitive`: round-trip, overwrite zeros old, dispose zeros all
- `DpapiDataProtector`: protect/unprotect round-trip
- `PasswordBridge`: registration/unregistration via DataContext changes
- `InstallerPage.GetPassword`: returns SensitiveBytes from registered PasswordBox, default for missing key

## Future Work

- Secure bundle-to-MSI property transport (Engine IPC / DPAPI temp files)
- Audit SharedState for other sensitive data patterns
