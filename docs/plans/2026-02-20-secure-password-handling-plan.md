# Secure Password Handling Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add framework-level secure password handling to FalkForge custom UI — passwords stored as DPAPI-encrypted bytes, never as plain strings.

**Architecture:** `SensitiveBytes` (IDisposable byte[] wrapper with zeroing) in Ui.Abstractions. `ISensitiveDataProtector` interface in Ui.Abstractions. `InstallerState` gains `SetSensitive`/`GetSensitive` with injected protector. `DpapiDataProtector` in Ui project. `PasswordBridge` attached property for XAML-declarative password access. MAS demo pages migrated to remove string password properties.

**Tech Stack:** .NET 10, WPF, System.Security.Cryptography.ProtectedData NuGet, xUnit

**Design:** `docs/plans/2026-02-20-secure-password-handling-design.md`

---

### Task 1: SensitiveBytes

**Files:**
- Create: `src/FalkForge.Ui.Abstractions/SensitiveBytes.cs`
- Test: `tests/FalkForge.Ui.Abstractions.Tests/SensitiveBytesTests.cs`

**Step 1: Write the failing tests**

File: `tests/FalkForge.Ui.Abstractions.Tests/SensitiveBytesTests.cs`

```csharp
namespace FalkForge.Ui.Abstractions.Tests;

using Xunit;

public sealed class SensitiveBytesTests
{
    [Fact]
    public void Span_returns_underlying_data()
    {
        var data = new byte[] { 1, 2, 3 };
        var sensitive = new SensitiveBytes(data);

        Assert.Equal(data, sensitive.Span.ToArray());
    }

    [Fact]
    public void Length_returns_byte_count()
    {
        var sensitive = new SensitiveBytes(new byte[] { 1, 2, 3 });

        Assert.Equal(3, sensitive.Length);
    }

    [Fact]
    public void IsEmpty_returns_true_for_null()
    {
        var sensitive = new SensitiveBytes(null!);

        Assert.True(sensitive.IsEmpty);
    }

    [Fact]
    public void IsEmpty_returns_true_for_empty_array()
    {
        var sensitive = new SensitiveBytes([]);

        Assert.True(sensitive.IsEmpty);
    }

    [Fact]
    public void IsEmpty_returns_false_for_data()
    {
        var sensitive = new SensitiveBytes(new byte[] { 1 });

        Assert.False(sensitive.IsEmpty);
    }

    [Fact]
    public void Default_is_empty()
    {
        SensitiveBytes sensitive = default;

        Assert.True(sensitive.IsEmpty);
        Assert.Equal(0, sensitive.Length);
    }

    [Fact]
    public void Dispose_zeroes_underlying_array()
    {
        var data = new byte[] { 0x41, 0x42, 0x43 };
        var sensitive = new SensitiveBytes(data);

        sensitive.Dispose();

        Assert.All(data, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Dispose_with_null_does_not_throw()
    {
        var sensitive = new SensitiveBytes(null!);

        sensitive.Dispose();
    }

    [Fact]
    public void Using_pattern_zeroes_on_scope_exit()
    {
        var data = new byte[] { 0x41, 0x42, 0x43 };

        using (var _ = new SensitiveBytes(data)) { }

        Assert.All(data, b => Assert.Equal(0, b));
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Ui.Abstractions.Tests/ --filter SensitiveBytesTests -v minimal`
Expected: FAIL — `SensitiveBytes` type does not exist

**Step 3: Write minimal implementation**

File: `src/FalkForge.Ui.Abstractions/SensitiveBytes.cs`

```csharp
namespace FalkForge.Ui.Abstractions;

using System.Security.Cryptography;

public readonly struct SensitiveBytes : IDisposable
{
    private readonly byte[]? _data;

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

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Ui.Abstractions.Tests/ --filter SensitiveBytesTests -v minimal`
Expected: PASS (9 tests)

**Step 5: Commit**

```bash
git add src/FalkForge.Ui.Abstractions/SensitiveBytes.cs tests/FalkForge.Ui.Abstractions.Tests/SensitiveBytesTests.cs
git commit -m "feat: add SensitiveBytes readonly struct with IDisposable zeroing"
```

---

### Task 2: ISensitiveDataProtector

**Files:**
- Create: `src/FalkForge.Ui.Abstractions/ISensitiveDataProtector.cs`

**Step 1: Create interface**

File: `src/FalkForge.Ui.Abstractions/ISensitiveDataProtector.cs`

```csharp
namespace FalkForge.Ui.Abstractions;

public interface ISensitiveDataProtector
{
    byte[] Protect(byte[] plainData);
    byte[] Unprotect(byte[] protectedData);
}
```

No tests needed — it's a pure interface.

**Step 2: Build**

Run: `dotnet build src/FalkForge.Ui.Abstractions/`
Expected: 0 warnings, 0 errors

**Step 3: Commit**

```bash
git add src/FalkForge.Ui.Abstractions/ISensitiveDataProtector.cs
git commit -m "feat: add ISensitiveDataProtector interface for sensitive data encryption"
```

---

### Task 3: InstallerState Sensitive Storage

**Files:**
- Modify: `src/FalkForge.Ui.Abstractions/InstallerState.cs`
- Test: `tests/FalkForge.Ui.Abstractions.Tests/InstallerStateTests.cs`

**Step 1: Write the failing tests**

Append to `tests/FalkForge.Ui.Abstractions.Tests/InstallerStateTests.cs` (after the last test, before closing brace on line 160):

```csharp
    [Fact]
    public void SetSensitive_and_GetSensitive_roundtrips()
    {
        var protector = new FakeProtector();
        var state = new InstallerState(protector);
        var data = new byte[] { 0x41, 0x42, 0x43 };

        state.SetSensitive("Password", data);
        using var result = state.GetSensitive("Password");

        Assert.Equal(data, result.Span.ToArray());
    }

    [Fact]
    public void GetSensitive_missing_key_returns_empty()
    {
        var state = new InstallerState(new FakeProtector());

        using var result = state.GetSensitive("Missing");

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void SetSensitive_overwrites_and_zeroes_old_value()
    {
        var protector = new TrackingProtector();
        var state = new InstallerState(protector);

        state.SetSensitive("Key", [1, 2, 3]);
        var firstProtected = protector.LastProtected!;

        state.SetSensitive("Key", [4, 5, 6]);

        Assert.All(firstProtected, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Dispose_zeroes_all_sensitive_entries()
    {
        var protector = new TrackingProtector();
        var state = new InstallerState(protector);
        state.SetSensitive("A", [1, 2, 3]);
        state.SetSensitive("B", [4, 5, 6]);
        var protectedA = protector.AllProtected[0];
        var protectedB = protector.AllProtected[1];

        state.Dispose();

        Assert.All(protectedA, b => Assert.Equal(0, b));
        Assert.All(protectedB, b => Assert.Equal(0, b));
    }

    [Fact]
    public void RemoveSensitive_zeroes_and_removes()
    {
        var protector = new TrackingProtector();
        var state = new InstallerState(protector);
        state.SetSensitive("Key", [1, 2, 3]);
        var protectedValue = protector.LastProtected!;

        var removed = state.RemoveSensitive("Key");

        Assert.True(removed);
        Assert.All(protectedValue, b => Assert.Equal(0, b));
        Assert.True(state.GetSensitive("Key").IsEmpty);
    }

    [Fact]
    public void RemoveSensitive_returns_false_for_missing_key()
    {
        var state = new InstallerState(new FakeProtector());

        Assert.False(state.RemoveSensitive("Missing"));
    }

    [Fact]
    public void Parameterless_constructor_still_works()
    {
        var state = new InstallerState();

        state.Set("Key", "Value");

        Assert.Equal("Value", state.Get<string>("Key"));
    }

    [Fact]
    public void SetSensitive_without_protector_throws()
    {
        var state = new InstallerState();

        Assert.Throws<InvalidOperationException>(() => state.SetSensitive("Key", [1, 2]));
    }

    private sealed class FakeProtector : ISensitiveDataProtector
    {
        public byte[] Protect(byte[] plainData) => [.. plainData];
        public byte[] Unprotect(byte[] protectedData) => [.. protectedData];
    }

    private sealed class TrackingProtector : ISensitiveDataProtector
    {
        public byte[]? LastProtected { get; private set; }
        public List<byte[]> AllProtected { get; } = [];

        public byte[] Protect(byte[] plainData)
        {
            var result = new byte[plainData.Length];
            Array.Copy(plainData, result, plainData.Length);
            LastProtected = result;
            AllProtected.Add(result);
            return result;
        }

        public byte[] Unprotect(byte[] protectedData) => [.. protectedData];
    }
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Ui.Abstractions.Tests/ --filter InstallerStateTests -v minimal`
Expected: FAIL — constructor overload and methods don't exist

**Step 3: Write implementation**

Modify `src/FalkForge.Ui.Abstractions/InstallerState.cs` to the following complete content:

```csharp
namespace FalkForge.Ui.Abstractions;

using System.Collections.Concurrent;
using System.Security.Cryptography;

public sealed class InstallerState : IDisposable
{
    private const string InstallDirectoryKey = "InstallDirectory";
    private readonly ConcurrentDictionary<string, object> _values = new();
    private readonly ConcurrentDictionary<string, byte[]> _sensitiveValues = new();
    private readonly ISensitiveDataProtector? _protector;

    public InstallerState() { }

    public InstallerState(ISensitiveDataProtector protector)
        => _protector = protector;

    public string? InstallDirectory
    {
        get => Get<string>(InstallDirectoryKey);
        set
        {
            if (value is null)
                _values.TryRemove(InstallDirectoryKey, out _);
            else
                Set(InstallDirectoryKey, value);
        }
    }

    public T? Get<T>(string key)
    {
        if (_values.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public void Set<T>(string key, T value) where T : notnull
        => _values[key] = value;

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool Remove(string key) => _values.TryRemove(key, out _);

    public void SetSensitive(string key, ReadOnlySpan<byte> data)
    {
        if (_protector is null)
            throw new InvalidOperationException(
                "No ISensitiveDataProtector configured. Use the InstallerState(ISensitiveDataProtector) constructor.");

        var protectedData = _protector.Protect(data.ToArray());

        if (_sensitiveValues.TryGetValue(key, out var old))
            CryptographicOperations.ZeroMemory(old);

        _sensitiveValues[key] = protectedData;
    }

    public SensitiveBytes GetSensitive(string key)
    {
        if (!_sensitiveValues.TryGetValue(key, out var protectedData) || _protector is null)
            return default;

        return new SensitiveBytes(_protector.Unprotect(protectedData));
    }

    public bool RemoveSensitive(string key)
    {
        if (!_sensitiveValues.TryRemove(key, out var protectedData))
            return false;

        CryptographicOperations.ZeroMemory(protectedData);
        return true;
    }

    public void Dispose()
    {
        foreach (var kvp in _sensitiveValues)
            CryptographicOperations.ZeroMemory(kvp.Value);
        _sensitiveValues.Clear();
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Ui.Abstractions.Tests/ --filter InstallerStateTests -v minimal`
Expected: PASS (all existing + 8 new tests)

**Step 5: Build full solution**

Run: `dotnet build`
Expected: 0 warnings. The parameterless constructor maintains backward compatibility with all existing `new InstallerState()` call sites.

**Step 6: Run all tests**

Run: `dotnet test`
Expected: All pass

**Step 7: Commit**

```bash
git add src/FalkForge.Ui.Abstractions/InstallerState.cs tests/FalkForge.Ui.Abstractions.Tests/InstallerStateTests.cs
git commit -m "feat: add SetSensitive/GetSensitive to InstallerState with encrypted storage"
```

---

### Task 4: DpapiDataProtector

**Files:**
- Modify: `src/FalkForge.Ui/FalkForge.Ui.csproj` (add NuGet reference)
- Create: `src/FalkForge.Ui/DpapiDataProtector.cs`
- Test: `tests/FalkForge.Ui.Tests/DpapiDataProtectorTests.cs`

**Step 1: Add NuGet package reference**

Add to `src/FalkForge.Ui/FalkForge.Ui.csproj` inside the `<ItemGroup>` with other PackageReferences (after line 11):

```xml
    <PackageReference Include="System.Security.Cryptography.ProtectedData" />
```

Note: Check `Directory.Packages.props` for central version management. If the package isn't listed there, add it with the latest stable version.

Run: `dotnet restore src/FalkForge.Ui/`

**Step 2: Write the failing test**

File: `tests/FalkForge.Ui.Tests/DpapiDataProtectorTests.cs`

```csharp
namespace FalkForge.Ui.Tests;

using FalkForge.Ui.Abstractions;
using Xunit;

public sealed class DpapiDataProtectorTests
{
    [Fact]
    public void Protect_and_Unprotect_roundtrips()
    {
        var protector = new DpapiDataProtector();
        var original = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encrypted = protector.Protect(original);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Protect_returns_different_bytes_than_input()
    {
        var protector = new DpapiDataProtector();
        var original = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encrypted = protector.Protect(original);

        Assert.NotEqual(original, encrypted);
    }

    [Fact]
    public void Protect_empty_array_roundtrips()
    {
        var protector = new DpapiDataProtector();

        var encrypted = protector.Protect([]);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Empty(decrypted);
    }
}
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Ui.Tests/ --filter DpapiDataProtectorTests -v minimal`
Expected: FAIL — `DpapiDataProtector` type does not exist

**Step 4: Write implementation**

File: `src/FalkForge.Ui/DpapiDataProtector.cs`

```csharp
namespace FalkForge.Ui;

using System.Security.Cryptography;
using FalkForge.Ui.Abstractions;

public sealed class DpapiDataProtector : ISensitiveDataProtector
{
    public byte[] Protect(byte[] plainData)
        => ProtectedData.Protect(plainData, entropy: null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData)
        => ProtectedData.Unprotect(protectedData, entropy: null, DataProtectionScope.CurrentUser);
}
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Ui.Tests/ --filter DpapiDataProtectorTests -v minimal`
Expected: PASS (3 tests)

**Step 6: Commit**

```bash
git add src/FalkForge.Ui/FalkForge.Ui.csproj src/FalkForge.Ui/DpapiDataProtector.cs tests/FalkForge.Ui.Tests/DpapiDataProtectorTests.cs
git commit -m "feat: add DpapiDataProtector using Windows DPAPI for sensitive data"
```

---

### Task 5: Wire DPAPI into InstallerApp

**Files:**
- Modify: `src/FalkForge.Ui/InstallerApp.cs:42` (inject protector into InstallerState)

**Step 1: Update InstallerApp**

In `src/FalkForge.Ui/InstallerApp.cs`, change line 42 from:

```csharp
        var sharedState = new InstallerState();
```

to:

```csharp
        var sharedState = new InstallerState(new DpapiDataProtector());
```

**Step 2: Build**

Run: `dotnet build`
Expected: 0 warnings

**Step 3: Run all tests**

Run: `dotnet test`
Expected: All pass

**Step 4: Commit**

```bash
git add src/FalkForge.Ui/InstallerApp.cs
git commit -m "feat: wire DpapiDataProtector into InstallerApp for encrypted SharedState"
```

---

### Task 6: PasswordBridge Attached Property

**Files:**
- Create: `src/FalkForge.Ui/PasswordBridge.cs`
- Modify: `src/FalkForge.Ui/InstallerPage.cs` (add password registration + GetPassword)
- Test: `tests/FalkForge.Ui.Tests/InstallerPageTests.cs`

**Step 1: Write the failing tests**

Append to `tests/FalkForge.Ui.Tests/InstallerPageTests.cs`.

Add a new test page class (after existing `PropertyTestPage`):

```csharp
public class PasswordTestPage : InstallerPage<TestView>
{
    public override string Title => "Password Test";

    public SensitiveBytes ReadPassword(string key) => GetPassword(key);
}
```

Add tests (after the last test in `InstallerPageTests`):

```csharp
    [Fact]
    public void GetPassword_unregistered_key_returns_empty()
    {
        var page = new PasswordTestPage();

        using var result = page.ReadPassword("Missing");

        Assert.True(result.IsEmpty);
    }

    [WpfFact]
    public void GetPassword_registered_passwordbox_returns_bytes()
    {
        var page = new PasswordTestPage();
        var box = new System.Windows.Controls.PasswordBox();
        box.Password = "secret";
        page.RegisterPasswordBox("Test", box);

        using var result = page.ReadPassword("Test");

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("secret"), result.Span.ToArray());
    }

    [WpfFact]
    public void UnregisterPasswordBox_removes_registration()
    {
        var page = new PasswordTestPage();
        var box = new System.Windows.Controls.PasswordBox();
        box.Password = "secret";
        page.RegisterPasswordBox("Test", box);

        page.UnregisterPasswordBox("Test");
        using var result = page.ReadPassword("Test");

        Assert.True(result.IsEmpty);
    }
```

Add `using FalkForge.Ui.Abstractions;` to the top of the file if not already present.

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Ui.Tests/ --filter InstallerPageTests -v minimal`
Expected: FAIL — `RegisterPasswordBox`, `UnregisterPasswordBox`, `GetPassword` don't exist

**Step 3: Write InstallerPage additions**

Modify `src/FalkForge.Ui/InstallerPage.cs`. Add to using directives:

```csharp
using System.Text;
using System.Windows.Controls;
```

Add inside the class, after the `SetField` overloads (after line 51):

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

**Step 4: Write PasswordBridge**

File: `src/FalkForge.Ui/PasswordBridge.cs`

```csharp
namespace FalkForge.Ui;

using System.Windows;
using System.Windows.Controls;

public static class PasswordBridge
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached(
            "Key",
            typeof(string),
            typeof(PasswordBridge),
            new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject obj)
        => (string?)obj.GetValue(KeyProperty);

    public static void SetKey(DependencyObject obj, string? value)
        => obj.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
            return;

        if (e.OldValue is string oldKey)
            Unregister(box, oldKey);

        if (e.NewValue is string newKey)
            Register(box, newKey);
    }

    private static void Register(PasswordBox box, string key)
    {
        box.Loaded += OnLoaded;
        box.Unloaded += OnUnloaded;

        if (box.IsLoaded)
            TryRegisterWithPage(box, key);
    }

    private static void Unregister(PasswordBox box, string key)
    {
        box.Loaded -= OnLoaded;
        box.Unloaded -= OnUnloaded;
        TryUnregisterFromPage(box, key);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && GetKey(box) is { } key)
            TryRegisterWithPage(box, key);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && GetKey(box) is { } key)
            TryUnregisterFromPage(box, key);
    }

    private static void TryRegisterWithPage(PasswordBox box, string key)
    {
        if (FindInstallerPage(box) is { } page)
            page.RegisterPasswordBox(key, box);
    }

    private static void TryUnregisterFromPage(PasswordBox box, string key)
    {
        if (FindInstallerPage(box) is { } page)
            page.UnregisterPasswordBox(key);
    }

    private static InstallerPage? FindInstallerPage(FrameworkElement element)
        => element.DataContext as InstallerPage;
}
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Ui.Tests/ --filter InstallerPageTests -v minimal`
Expected: PASS (all existing + 3 new tests)

**Step 6: Build full solution**

Run: `dotnet build`
Expected: 0 warnings

**Step 7: Commit**

```bash
git add src/FalkForge.Ui/InstallerPage.cs src/FalkForge.Ui/PasswordBridge.cs tests/FalkForge.Ui.Tests/InstallerPageTests.cs
git commit -m "feat: add PasswordBridge attached property and GetPassword to InstallerPage"
```

---

### Task 7: Migrate DatabaseConnectionSettingsPage

**Files:**
- Modify: `demo/MAS/Pages/DatabaseConnectionSettingsPage.cs` (remove `_password` field and `Password` property)
- Modify: `demo/MAS/Views/DatabaseConnectionSettingsView.xaml` (add `PasswordBridge.Key`, remove `PasswordChanged` handler)
- Modify: `demo/MAS/Views/DatabaseConnectionSettingsView.xaml.cs` (remove `PasswordBox_PasswordChanged`)

**Step 1: Update XAML**

In `demo/MAS/Views/DatabaseConnectionSettingsView.xaml`:

Add namespace at line 1 (inside the UserControl tag):
```xml
             xmlns:ui="clr-namespace:FalkForge.Ui;assembly=FalkForge.Ui"
```

Replace lines 41-43:
```xml
                        <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                     BorderBrush="#AAAAAA"
                                     PasswordChanged="PasswordBox_PasswordChanged"/>
```
with:
```xml
                        <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                     BorderBrush="#AAAAAA"
                                     ui:PasswordBridge.Key="DbPassword"/>
```

**Step 2: Update code-behind**

In `demo/MAS/Views/DatabaseConnectionSettingsView.xaml.cs`, remove lines 11-15 (the `PasswordBox_PasswordChanged` method):

```csharp
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is DatabaseConnectionSettingsPage page)
            page.Password = ((PasswordBox)sender).Password;
    }
```

**Step 3: Update page**

In `demo/MAS/Pages/DatabaseConnectionSettingsPage.cs`:

Remove the `_password` field (line 7):
```csharp
    private string _password = string.Empty;
```

Remove the `Password` property (lines 49-53):
```csharp
    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }
```

Update `TestConnectionAsync` to use `GetPassword` instead of `_password`. Change:
```csharp
        var result = await tester.TestConnectionAsync(
            DatabaseServer, DatabaseName, IntegratedSecurity, UserName, Password);
```
to:
```csharp
        using var pw = GetPassword("DbPassword");
        var passwordStr = pw.IsEmpty ? string.Empty : System.Text.Encoding.UTF8.GetString(pw.Span);
        var result = await tester.TestConnectionAsync(
            DatabaseServer, DatabaseName, IntegratedSecurity, UserName, passwordStr);
```

Update `OnNext` to use `SetSensitive`. Change:
```csharp
        SharedState.Set("DbPassword", _password);
```
to:
```csharp
        using var pw = GetPassword("DbPassword");
        if (!pw.IsEmpty)
            SharedState.SetSensitive("DbPassword", pw.Span);
```

Note: `SharedState.Set("DbUserName", _userName)` stays as-is since username is not sensitive.

**Step 4: Build**

Run: `dotnet build`
Expected: 0 warnings

**Step 5: Commit**

```bash
git add demo/MAS/Pages/DatabaseConnectionSettingsPage.cs demo/MAS/Views/DatabaseConnectionSettingsView.xaml demo/MAS/Views/DatabaseConnectionSettingsView.xaml.cs
git commit -m "refactor: migrate DatabaseConnectionSettingsPage to PasswordBridge"
```

---

### Task 8: Migrate MultiServerAdvancedSettingsPage

**Files:**
- Modify: `demo/MAS/Pages/MultiServerAdvancedSettingsPage.cs` (remove `_servicePassword` field and `ServicePassword` property)
- Modify: `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml` (add `PasswordBridge.Key`, remove `PasswordChanged`)
- Modify: `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml.cs` (remove `PasswordBox_PasswordChanged`)

**Step 1: Update XAML**

In `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml`:

Add namespace in the UserControl tag:
```xml
             xmlns:ui="clr-namespace:FalkForge.Ui;assembly=FalkForge.Ui"
```

Replace lines 57-59:
```xml
                    <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                 BorderBrush="#AAAAAA"
                                 PasswordChanged="PasswordBox_PasswordChanged"/>
```
with:
```xml
                    <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                 BorderBrush="#AAAAAA"
                                 ui:PasswordBridge.Key="MultiServerServicePassword"/>
```

**Step 2: Update code-behind**

In `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml.cs`, remove lines 11-15 (the `PasswordBox_PasswordChanged` method):

```csharp
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MultiServerAdvancedSettingsPage page)
            page.ServicePassword = ((PasswordBox)sender).Password;
    }
```

**Step 3: Update page**

In `demo/MAS/Pages/MultiServerAdvancedSettingsPage.cs`:

Remove the `_servicePassword` field (line 6):
```csharp
    private string _servicePassword = string.Empty;
```

Remove the `ServicePassword` property (lines 39-43):
```csharp
    public string ServicePassword
    {
        get => _servicePassword;
        set => SetField(ref _servicePassword, value);
    }
```

Update `OnNext` to use `SetSensitive`. Change:
```csharp
        SharedState.Set("MultiServerServicePassword", _servicePassword);
```
to:
```csharp
        using var pw = GetPassword("MultiServerServicePassword");
        if (!pw.IsEmpty)
            SharedState.SetSensitive("MultiServerServicePassword", pw.Span);
```

**Step 4: Build**

Run: `dotnet build`
Expected: 0 warnings

**Step 5: Commit**

```bash
git add demo/MAS/Pages/MultiServerAdvancedSettingsPage.cs demo/MAS/Views/MultiServerAdvancedSettingsView.xaml demo/MAS/Views/MultiServerAdvancedSettingsView.xaml.cs
git commit -m "refactor: migrate MultiServerAdvancedSettingsPage to PasswordBridge"
```

---

### Task 9: Migrate MultiServerExAdvancedSettingsPage

**Files:**
- Modify: `demo/MAS/Pages/MultiServerExAdvancedSettingsPage.cs`
- Modify: `demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml`
- Modify: `demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml.cs`

**Step 1: Update XAML**

In `demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml`:

Add namespace:
```xml
             xmlns:ui="clr-namespace:FalkForge.Ui;assembly=FalkForge.Ui"
```

Replace PasswordBox (same location as MultiServer — lines 57-59):
```xml
                    <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                 BorderBrush="#AAAAAA"
                                 ui:PasswordBridge.Key="MultiServerExServicePassword"/>
```

**Step 2: Update code-behind**

Remove `PasswordBox_PasswordChanged` method (lines 11-15).

**Step 3: Update page**

Remove `_servicePassword` field and `ServicePassword` property.

Update `OnNext`:
```csharp
        using var pw = GetPassword("MultiServerExServicePassword");
        if (!pw.IsEmpty)
            SharedState.SetSensitive("MultiServerExServicePassword", pw.Span);
```

Remove the old line: `SharedState.Set("MultiServerExServicePassword", _servicePassword);`

**Step 4: Build and test**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, all tests pass

**Step 5: Commit**

```bash
git add demo/MAS/Pages/MultiServerExAdvancedSettingsPage.cs demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml.cs
git commit -m "refactor: migrate MultiServerExAdvancedSettingsPage to PasswordBridge"
```

---

### Task 10: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update CLAUDE.md**

Add `SensitiveBytes`, `ISensitiveDataProtector`, `DpapiDataProtector`, and `PasswordBridge` to the relevant sections:

Under "Key Patterns & Locations", add:

```markdown
### SensitiveBytes -- `src/FalkForge.Ui.Abstractions/SensitiveBytes.cs`
Readonly struct wrapping `byte[]`. IDisposable — zeros memory via `CryptographicOperations.ZeroMemory` on dispose. Always use via `using` pattern. For secure password and credential handling.

### ISensitiveDataProtector -- `src/FalkForge.Ui.Abstractions/ISensitiveDataProtector.cs`
Interface for encrypt/decrypt of sensitive byte arrays. Implemented by `DpapiDataProtector` in Ui project.

### DpapiDataProtector -- `src/FalkForge.Ui/DpapiDataProtector.cs`
Windows DPAPI implementation of `ISensitiveDataProtector`. Uses `DataProtectionScope.CurrentUser`. No admin required.

### PasswordBridge -- `src/FalkForge.Ui/PasswordBridge.cs`
WPF attached property for secure password access. XAML: `<PasswordBox ui:PasswordBridge.Key="keyName"/>`. Page reads via `GetPassword("keyName")` which returns `SensitiveBytes`. Password never stored as string property.
```

Under "InstallerState" section, add note:
```markdown
### InstallerState -- `src/FalkForge.Ui.Abstractions/InstallerState.cs`
Thread-safe state store. `Get<T>(key)` / `Set<T>(key, value)` for general data. `SetSensitive(key, span)` / `GetSensitive(key)` for DPAPI-encrypted sensitive data. Implements `IDisposable` to zero all sensitive entries. Requires `ISensitiveDataProtector` via constructor for sensitive operations.
```

**Step 2: Build and test**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, all tests pass

**Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with secure password handling types"
```

---

## Verification Checklist

After all tasks:

1. `dotnet build` — 0 warnings
2. `dotnet test` — all pass including ~20 new tests
3. No `string` password properties remain in MAS demo pages
4. `PasswordBridge.Key` used in all 3 XAML views
5. `InstallerState` uses DPAPI encryption for sensitive values
6. All sensitive byte arrays zeroed on dispose
