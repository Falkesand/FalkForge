# Secure Property Protocol Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Wire SetProperty/SetSecureProperty through the named pipe protocol to MSI execution via MsiInstallProduct P/Invoke.

**Architecture:** Two new protocol messages flow from UI through EngineClient, named pipe, EngineHost, VariableStore, Planner, MsiExecutor, to MsiInstallProduct P/Invoke. Secure properties never appear on a process command line.

**Tech Stack:** C# .NET 10, msi.dll P/Invoke (LibraryImport), xUnit

**Design doc:** `docs/plans/2026-02-22-secure-property-protocol-design.md`

---

## Task 1: Add MSI P/Invoke Declarations to Platform.Windows

**Files:**
- `src/FalkForge.Platform.Windows/NativeMethods.Msi.cs` — if this file exists, add to it; otherwise check for the existing P/Invoke pattern in the project and create a new file following that pattern
- `tests/FalkForge.Platform.Windows.Tests/` or nearest test project — compilation verification test

**Steps:**

1. **Explore** the existing `src/FalkForge.Platform.Windows/` project to find the current P/Invoke file naming and pattern. Note: `Compiler.Msi` has its own `Interop/NativeMethods.Msi.cs` for database operations — the engine-side declarations belong in `Platform.Windows`.

2. **Write failing test** — a test that references `NativeMethods.MsiInstallProductW`, `NativeMethods.MsiConfigureProductW`, and `NativeMethods.MsiSetInternalUI`. The test verifies the declarations compile and have the expected parameter types via reflection:

```csharp
[Fact]
public void MsiInstallProductW_has_correct_signature()
{
    var method = typeof(NativeMethods).GetMethod("MsiInstallProductW",
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    Assert.NotNull(method);
    var parameters = method!.GetParameters();
    Assert.Equal(2, parameters.Length);
    Assert.Equal(typeof(string), parameters[0].ParameterType);
    Assert.Equal(typeof(string), parameters[1].ParameterType);
    Assert.Equal(typeof(uint), method.ReturnType);
}
```

3. **Implement** the P/Invoke declarations:

```csharp
// In the appropriate NativeMethods file in Platform.Windows
[LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial uint MsiInstallProductW(string szPackagePath, string? szCommandLine);

[LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial uint MsiConfigureProductW(string szProductCode, int iInstallLevel, int iInstallState);

[LibraryImport("msi.dll")]
internal static partial int MsiSetInternalUI(int dwUILevel, nint phWnd);
```

4. **Add constants** in the same file or a companion constants class:

```csharp
internal const int INSTALLLEVEL_DEFAULT = 0;
internal const int INSTALLSTATE_ABSENT = 2;
internal const int INSTALLUILEVEL_NONE = 2;
internal const uint ERROR_SUCCESS = 0;
internal const uint ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
```

5. **Add IMsiApi abstraction** for testability (in `FalkForge.Engine` or `Platform.Windows` depending on layering):

```csharp
internal interface IMsiApi
{
    uint InstallProduct(string packagePath, string? commandLine);
    uint ConfigureProduct(string productCode, int installLevel, int installState);
    int SetInternalUI(int uiLevel, nint window);
}
```

6. **Run tests** — verify compilation and signature assertions pass.

**Expected Outcome:** Three P/Invoke declarations in Platform.Windows, an IMsiApi abstraction, MSI constants, and passing signature tests.

---

## Task 2: Add Protocol Message Types

**Files:**
- `src/FalkForge.Engine.Protocol/Messages/SetPropertyMessage.cs` — **new file**
- `src/FalkForge.Engine.Protocol/Messages/SetSecurePropertyMessage.cs` — **new file**
- `src/FalkForge.Engine.Protocol/Messages/MessageType.cs` — add two enum values
- `src/FalkForge.Engine.Protocol/Serialization/MessageSerializer.cs` — add serialization cases
- `src/FalkForge.Engine.Protocol/Serialization/MessageDeserializer.cs` — add deserialization cases
- `tests/FalkForge.Engine.Protocol.Tests/` — roundtrip tests

**Steps:**

1. **Read** the existing `MessageType.cs` enum to find the current numbering and the existing message files (e.g., `SetInstallDirectoryMessage.cs`) to follow the pattern exactly.

2. **Write failing tests** — roundtrip serialization tests:

```csharp
[Fact]
public void SetPropertyMessage_roundtrip()
{
    var original = new SetPropertyMessage("INSTALLFOLDER", @"C:\MyApp");
    var bytes = MessageSerializer.Serialize(original);
    var deserialized = MessageDeserializer.Deserialize(bytes);
    var result = Assert.IsType<SetPropertyMessage>(deserialized);
    Assert.Equal("INSTALLFOLDER", result.Name);
    Assert.Equal(@"C:\MyApp", result.Value);
}

[Fact]
public void SetSecurePropertyMessage_roundtrip()
{
    var secretBytes = Encoding.UTF8.GetBytes("s3cret_p@ssw0rd");
    var original = new SetSecurePropertyMessage("DB_PASSWORD", secretBytes);
    var bytes = MessageSerializer.Serialize(original);
    var deserialized = MessageDeserializer.Deserialize(bytes);
    var result = Assert.IsType<SetSecurePropertyMessage>(deserialized);
    Assert.Equal("DB_PASSWORD", result.Name);
    Assert.Equal(secretBytes, result.Value);
}

[Fact]
public void SetSecurePropertyMessage_empty_value_roundtrip()
{
    var original = new SetSecurePropertyMessage("EMPTY", []);
    var bytes = MessageSerializer.Serialize(original);
    var deserialized = MessageDeserializer.Deserialize(bytes);
    var result = Assert.IsType<SetSecurePropertyMessage>(deserialized);
    Assert.Equal("EMPTY", result.Name);
    Assert.Empty(result.Value);
}
```

3. **Add enum values** to `MessageType`:

```csharp
SetProperty = 0x0208,
SetSecureProperty = 0x0209,
```

4. **Create SetPropertyMessage.cs:**

```csharp
namespace FalkForge.Engine.Protocol.Messages;

public sealed class SetPropertyMessage(string name, string value) : IProtocolMessage
{
    public MessageType Type => MessageType.SetProperty;
    public string Name { get; } = name;
    public string Value { get; } = value;
}
```

5. **Create SetSecurePropertyMessage.cs:**

```csharp
namespace FalkForge.Engine.Protocol.Messages;

public sealed class SetSecurePropertyMessage(string name, byte[] value) : IProtocolMessage
{
    public MessageType Type => MessageType.SetSecureProperty;
    public string Name { get; } = name;
    public byte[] Value { get; } = value;
}
```

6. **Add serialization** in `MessageSerializer.cs` — follow the existing pattern for string-based messages. For `SetSecurePropertyMessage`, write `int length` + raw bytes (not a UTF-16 string):

```csharp
// SetPropertyMessage
case SetPropertyMessage msg:
    WriteString(writer, msg.Name);
    WriteString(writer, msg.Value);
    break;

// SetSecurePropertyMessage
case SetSecurePropertyMessage msg:
    WriteString(writer, msg.Name);
    writer.Write(msg.Value.Length);
    writer.Write(msg.Value);
    break;
```

7. **Add deserialization** in `MessageDeserializer.cs`:

```csharp
MessageType.SetProperty => new SetPropertyMessage(ReadString(reader), ReadString(reader)),
MessageType.SetSecureProperty => DeserializeSecureProperty(reader),

// Helper:
private static SetSecurePropertyMessage DeserializeSecureProperty(BinaryReader reader)
{
    var name = ReadString(reader);
    var length = reader.ReadInt32();
    var value = reader.ReadBytes(length);
    return new SetSecurePropertyMessage(name, value);
}
```

8. **Run tests** — all three roundtrip tests pass.

**Expected Outcome:** Two new message types, enum values, serializer/deserializer support, and passing roundtrip tests.

---

## Task 3: Wire EngineClient in UI Project

**Files:**
- `src/FalkForge.Ui/EngineClient.cs` — replace `NotSupportedException` stubs
- `tests/FalkForge.Ui.Tests/` — EngineClient property tests

**Steps:**

1. **Read** `src/FalkForge.Ui/EngineClient.cs` to find the existing `SetProperty` and `SetSecureProperty` method stubs and the pattern used by `SendSetInstallDirectoryAsync` or similar methods.

2. **Write failing tests** — verify EngineClient creates and sends the correct message types. May need a mock `PipeClient` or use the existing test infrastructure:

```csharp
[Fact]
public void SetProperty_sends_SetPropertyMessage()
{
    // Arrange: EngineClient with mock pipe
    // Act: client.SetProperty("PROP", "VALUE")
    // Assert: mock received a SetPropertyMessage with Name="PROP", Value="VALUE"
}

[Fact]
public void SetSecureProperty_sends_SetSecurePropertyMessage_and_zeros_copy()
{
    // Arrange: EngineClient with mock pipe, SensitiveBytes with known content
    // Act: client.SetSecureProperty("SECRET", sensitiveBytes)
    // Assert: mock received SetSecurePropertyMessage with correct name and value bytes
}
```

3. **Implement SetProperty:**

```csharp
public void SetProperty(string name, string value)
{
    var message = new SetPropertyMessage(name, value);
    _pipe.SendAsync(MessageSerializer.Serialize(message)).GetAwaiter().GetResult();
}
```

4. **Implement SetSecureProperty** — copy `SensitiveBytes.Span` to `byte[]`, create message, send, zero the copy:

```csharp
public void SetSecureProperty(string name, SensitiveBytes value)
{
    var copy = value.Span.ToArray();
    try
    {
        var message = new SetSecurePropertyMessage(name, copy);
        _pipe.SendAsync(MessageSerializer.Serialize(message)).GetAwaiter().GetResult();
    }
    finally
    {
        CryptographicOperations.ZeroMemory(copy);
    }
}
```

5. **Run tests** — verify message creation and send behavior.

**Expected Outcome:** `SetProperty` and `SetSecureProperty` on `EngineClient` send the correct protocol messages over pipe A.

---

## Task 4: Wire EngineHost Message Dispatch

**Files:**
- `src/FalkForge.Engine/EngineHost.cs` — add cases in `HandleUiMessageAsync`
- `src/FalkForge.Engine/Variables/VariableStore.cs` — add `SetSecret`, `GetSecret`, `SecureVariable`, `_userProperties` tracking
- `src/FalkForge.Engine/Variables/SecureVariable.cs` — **new file**
- `tests/FalkForge.Engine.Tests/` — EngineHost and VariableStore tests

**Steps:**

1. **Read** `src/FalkForge.Engine/EngineHost.cs` to find `HandleUiMessageAsync` and the existing message dispatch pattern (likely a `switch` on `MessageType`).

2. **Read** `src/FalkForge.Engine/Variables/VariableStore.cs` to understand the current API surface.

3. **Write failing tests:**

```csharp
// VariableStore tests
[Fact]
public void SetSecret_stores_SecureVariable()
{
    var store = new VariableStore();
    var secret = Encoding.UTF8.GetBytes("password123");
    store.SetSecret("DB_PASS", secret);

    var retrieved = store.GetSecret("DB_PASS");
    Assert.NotNull(retrieved);
    Assert.Equal(secret, retrieved!.Value.ToArray());
}

[Fact]
public void SetSecret_disposes_previous_value()
{
    var store = new VariableStore();
    store.SetSecret("KEY", [1, 2, 3]);
    store.SetSecret("KEY", [4, 5, 6]);

    var retrieved = store.GetSecret("KEY");
    Assert.Equal(new byte[] { 4, 5, 6 }, retrieved!.Value.ToArray());
}

[Fact]
public void Set_from_ui_tracks_user_property()
{
    var store = new VariableStore();
    store.Set("MYPROP", "myval", isUserProperty: true);

    Assert.True(store.IsUserProperty("MYPROP"));
    Assert.False(store.IsUserProperty("SomeBuiltIn"));
}

// EngineHost tests
[Fact]
public async Task HandleUiMessage_SetProperty_during_Planning_sets_variable()
{
    // Arrange: EngineHost in Planning phase with mock context
    // Act: dispatch SetPropertyMessage("PROP", "VAL")
    // Assert: context.Variables.Get("PROP") == "VAL"
}

[Fact]
public async Task HandleUiMessage_SetProperty_during_Applying_rejects()
{
    // Arrange: EngineHost in Applying phase
    // Act: dispatch SetPropertyMessage
    // Assert: error response or ignored (per design)
}
```

4. **Create SecureVariable.cs:**

```csharp
namespace FalkForge.Engine.Variables;

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

    public ReadOnlySpan<byte> Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _data;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_data);
        _handle.Free();
    }
}
```

5. **Update VariableStore** — add `SetSecret`, `GetSecret`, `_secretVariables` dictionary, `_userProperties` HashSet, `IsUserProperty` method, overload `Set` with `isUserProperty` parameter, `GetUserPropertyNames()` enumerator.

6. **Update EngineHost.HandleUiMessageAsync** — add switch cases:

```csharp
case SetPropertyMessage msg when IsInPhaseForConfiguration():
    context.Variables.Set(msg.Name, msg.Value, isUserProperty: true);
    break;

case SetSecurePropertyMessage msg when IsInPhaseForConfiguration():
    context.Variables.SetSecret(msg.Name, msg.Value);
    break;

case SetPropertyMessage or SetSecurePropertyMessage:
    // Reject — not in valid phase
    await SendErrorAsync("Properties can only be set during Initializing, Detecting, or Planning phases.");
    break;
```

7. **Run tests** — all VariableStore and EngineHost tests pass.

**Expected Outcome:** EngineHost dispatches property messages to VariableStore. SecureVariable provides pinned, zeroable storage. User-set properties are tracked separately from built-ins.

---

## Task 5: Wire Planner Property Propagation

**Files:**
- `src/FalkForge.Engine/Planning/Planner.cs` — read user properties from VariableStore into PlanAction
- `src/FalkForge.Engine/Planning/PlanAction.cs` — add `Properties` dictionary and `SecretPropertyNames` set
- `tests/FalkForge.Engine.Tests/` — Planner property propagation tests

**Steps:**

1. **Read** `src/FalkForge.Engine/Planning/Planner.cs` and `PlanAction.cs` to understand the current planning model.

2. **Write failing tests:**

```csharp
[Fact]
public void Planner_copies_user_properties_to_PlanAction()
{
    // Arrange: VariableStore with user properties set
    var variables = new VariableStore();
    variables.Set("INSTALLFOLDER", @"C:\MyApp", isUserProperty: true);
    variables.Set("ALLUSERS", "1", isUserProperty: true);

    // Act: Plan
    var plan = planner.CreatePlan(manifest, variables, InstallAction.Install);

    // Assert: MSI PlanAction has both properties
    var msiAction = plan.Actions.OfType<MsiPlanAction>().First();
    Assert.Equal(@"C:\MyApp", msiAction.Properties["INSTALLFOLDER"]);
    Assert.Equal("1", msiAction.Properties["ALLUSERS"]);
}

[Fact]
public void Planner_tracks_secret_property_names_in_PlanAction()
{
    var variables = new VariableStore();
    variables.SetSecret("DB_PASSWORD", Encoding.UTF8.GetBytes("secret"));

    var plan = planner.CreatePlan(manifest, variables, InstallAction.Install);

    var msiAction = plan.Actions.OfType<MsiPlanAction>().First();
    Assert.Contains("DB_PASSWORD", msiAction.SecretPropertyNames);
}
```

3. **Update PlanAction** (or MsiPlanAction if there's a subclass) to include:

```csharp
public Dictionary<string, string> Properties { get; init; } = [];
public HashSet<string> SecretPropertyNames { get; init; } = [];
```

4. **Update Planner** — after building the action list, for each MSI action, iterate `variables.GetUserPropertyNames()` and populate `Properties`. For secrets, add names to `SecretPropertyNames` (values resolved at execution time by MsiExecutor).

5. **Run tests** — property propagation verified.

**Expected Outcome:** PlanAction instances carry user-set properties and secret property name references for MsiExecutor to resolve.

---

## Task 6: Refactor MsiExecutor to Use MsiInstallProduct P/Invoke

**Files:**
- `src/FalkForge.Engine/Execution/MsiExecutor.cs` — replace Process.Start with IMsiApi
- `tests/FalkForge.Engine.Tests/` — MsiExecutor tests with mock IMsiApi

**Steps:**

1. **Read** `src/FalkForge.Engine/Execution/MsiExecutor.cs` to understand the current `IProcessRunner`-based execution.

2. **Write failing tests:**

```csharp
[Fact]
public async Task MsiExecutor_calls_MsiInstallProduct_with_property_string()
{
    // Arrange
    var mockApi = new MockMsiApi();
    var executor = new MsiExecutor(mockApi, variableStore, logger);
    var action = new MsiPlanAction
    {
        PackagePath = @"C:\pkg.msi",
        Properties = new() { ["INSTALLFOLDER"] = @"C:\App", ["ALLUSERS"] = "1" }
    };

    // Act
    var result = await executor.ExecuteAsync(action, CancellationToken.None);

    // Assert
    Assert.Equal(@"C:\pkg.msi", mockApi.LastPackagePath);
    Assert.Contains("INSTALLFOLDER=C:\\App", mockApi.LastCommandLine);
    Assert.Contains("ALLUSERS=1", mockApi.LastCommandLine);
    Assert.Equal(ExecutionOutcome.Success, result.Outcome);
}

[Fact]
public async Task MsiExecutor_resolves_secrets_and_zeros_after_call()
{
    var mockApi = new MockMsiApi();
    var variables = new VariableStore();
    variables.SetSecret("DB_PASS", Encoding.UTF8.GetBytes("secret123"));
    var executor = new MsiExecutor(mockApi, variables, logger);
    var action = new MsiPlanAction
    {
        PackagePath = @"C:\pkg.msi",
        Properties = [],
        SecretPropertyNames = ["DB_PASS"]
    };

    await executor.ExecuteAsync(action, CancellationToken.None);

    // Secret was included in property string
    Assert.Contains("DB_PASS=secret123", mockApi.LastCommandLine);
}

[Fact]
public async Task MsiExecutor_sets_silent_ui_before_install()
{
    var mockApi = new MockMsiApi();
    var executor = new MsiExecutor(mockApi, variableStore, logger);
    await executor.ExecuteAsync(action, CancellationToken.None);

    Assert.True(mockApi.SetInternalUICalled);
    Assert.Equal(2, mockApi.LastUiLevel); // INSTALLUILEVEL_NONE
}

[Fact]
public async Task MsiExecutor_maps_3010_to_success_with_reboot()
{
    var mockApi = new MockMsiApi { InstallReturnCode = 3010 };
    var executor = new MsiExecutor(mockApi, variableStore, logger);

    var result = await executor.ExecuteAsync(action, CancellationToken.None);

    Assert.Equal(ExecutionOutcome.SuccessRebootRequired, result.Outcome);
}
```

3. **Inject IMsiApi** into MsiExecutor constructor (alongside existing IProcessRunner for non-MSI packages).

4. **Replace the MSI execution path:**

```csharp
// Before: processRunner.RunAsync("msiexec.exe", $"/i \"{action.PackagePath}\" /qn {propertyString}")
// After:
msiApi.SetInternalUI(INSTALLUILEVEL_NONE, nint.Zero);
var commandLine = BuildPropertyString(action, variableStore);
try
{
    var exitCode = msiApi.InstallProduct(action.PackagePath, commandLine);
    return MapExitCode(exitCode);
}
finally
{
    ZeroString(commandLine);
}
```

5. **Implement `BuildPropertyString`** — concatenates regular properties and resolved secrets:

```csharp
private static string BuildPropertyString(MsiPlanAction action, VariableStore variables)
{
    var parts = new List<string>();
    foreach (var (name, value) in action.Properties)
        parts.Add($"{name}={value}");
    foreach (var secretName in action.SecretPropertyNames)
    {
        var secret = variables.GetSecret(secretName);
        if (secret is not null)
            parts.Add($"{secretName}={Encoding.UTF8.GetString(secret.Value)}");
    }
    return string.Join(" ", parts);
}
```

6. **Implement `MapExitCode`:**

```csharp
private static ExecutionResult MapExitCode(uint exitCode) => exitCode switch
{
    0 => new(ExecutionOutcome.Success),
    3010 => new(ExecutionOutcome.SuccessRebootRequired),
    1602 => new(ExecutionOutcome.Cancelled),
    _ => new(ExecutionOutcome.Failed, $"MsiInstallProduct returned {exitCode}")
};
```

7. **Keep IProcessRunner** for MSU, MSP, EXE bundle packages — only MSI install uses the new path.

8. **Run tests** — all MsiExecutor tests pass with MockMsiApi.

**Expected Outcome:** MsiExecutor uses in-process P/Invoke for MSI installs. No `msiexec.exe` process spawning. Secrets resolved at execution time and zeroed after.

---

## Task 7: Refactor MsiInstallCommand (Elevated)

**Files:**
- `src/FalkForge.Engine.Elevation/Commands/MsiInstallCommand.cs` — replace Process.Start with P/Invoke
- `tests/FalkForge.Engine.Elevation.Tests/` — updated tests

**Steps:**

1. **Read** `src/FalkForge.Engine.Elevation/Commands/MsiInstallCommand.cs` to understand the current implementation.

2. **Write failing tests:**

```csharp
[Fact]
public async Task MsiInstallCommand_calls_MsiInstallProduct()
{
    var mockApi = new MockMsiApi();
    var command = new MsiInstallCommand(mockApi);

    var result = await command.ExecuteAsync(new MsiInstallRequest
    {
        PackagePath = @"C:\pkg.msi",
        PropertyString = "INSTALLFOLDER=C:\\App"
    });

    Assert.Equal(@"C:\pkg.msi", mockApi.LastPackagePath);
    Assert.Equal("INSTALLFOLDER=C:\\App", mockApi.LastCommandLine);
    Assert.True(result.Success);
}

[Fact]
public async Task MsiInstallCommand_sets_silent_ui()
{
    var mockApi = new MockMsiApi();
    var command = new MsiInstallCommand(mockApi);

    await command.ExecuteAsync(new MsiInstallRequest { PackagePath = @"C:\pkg.msi" });

    Assert.Equal(2, mockApi.LastUiLevel);
}
```

3. **Replace implementation:**

```csharp
// Before: Process.Start("msiexec.exe", $"/i \"{request.PackagePath}\" /qn {request.PropertyString}")
// After:
_msiApi.SetInternalUI(INSTALLUILEVEL_NONE, nint.Zero);
var exitCode = _msiApi.InstallProduct(request.PackagePath, request.PropertyString);
// Zero the property string if it was received as char[]
return new MsiInstallResult(exitCode);
```

4. **Run tests** — MsiInstallCommand passes with mock.

**Expected Outcome:** Elevated MSI install uses P/Invoke instead of process spawning.

---

## Task 8: Refactor MsiUninstallCommand (Elevated)

**Files:**
- `src/FalkForge.Engine.Elevation/Commands/MsiUninstallCommand.cs` — replace Process.Start with MsiConfigureProduct
- `tests/FalkForge.Engine.Elevation.Tests/` — updated tests

**Steps:**

1. **Read** `src/FalkForge.Engine.Elevation/Commands/MsiUninstallCommand.cs`.

2. **Write failing tests:**

```csharp
[Fact]
public async Task MsiUninstallCommand_calls_MsiConfigureProduct_with_absent_state()
{
    var mockApi = new MockMsiApi();
    var command = new MsiUninstallCommand(mockApi);

    var result = await command.ExecuteAsync(new MsiUninstallRequest
    {
        ProductCode = "{12345678-1234-1234-1234-123456789012}"
    });

    Assert.Equal("{12345678-1234-1234-1234-123456789012}", mockApi.LastProductCode);
    Assert.Equal(0, mockApi.LastInstallLevel);  // INSTALLLEVEL_DEFAULT
    Assert.Equal(2, mockApi.LastInstallState);   // INSTALLSTATE_ABSENT
    Assert.True(result.Success);
}

[Fact]
public async Task MsiUninstallCommand_sets_silent_ui()
{
    var mockApi = new MockMsiApi();
    var command = new MsiUninstallCommand(mockApi);

    await command.ExecuteAsync(new MsiUninstallRequest
    {
        ProductCode = "{12345678-1234-1234-1234-123456789012}"
    });

    Assert.Equal(2, mockApi.LastUiLevel);
}
```

3. **Replace implementation:**

```csharp
// Before: Process.Start("msiexec.exe", $"/x {request.ProductCode} /qn")
// After:
_msiApi.SetInternalUI(INSTALLUILEVEL_NONE, nint.Zero);
var exitCode = _msiApi.ConfigureProduct(request.ProductCode, INSTALLLEVEL_DEFAULT, INSTALLSTATE_ABSENT);
return new MsiUninstallResult(exitCode);
```

4. **Run tests** — MsiUninstallCommand passes with mock.

**Expected Outcome:** Elevated MSI uninstall uses `MsiConfigureProduct` P/Invoke instead of process spawning.

---

## Task 9: Build and Full Test Suite

**Files:** None (verification only)

**Steps:**

1. **Run `dotnet build`** on the solution root — must produce 0 warnings (TreatWarningsAsErrors).

2. **Run `dotnet test`** on the solution root — all ~1900+ tests must pass.

3. **If any failures**, investigate and fix. Common issues:
   - Missing `using` statements for new message types
   - Constructor signature changes in MsiExecutor or commands requiring DI updates
   - Test mocks not matching updated interfaces

4. **Verify no regressions** in existing functionality by confirming the full test count hasn't decreased.

**Expected Outcome:** Clean build, zero warnings, all tests passing.

---

## Task 10: Update Documentation

**Files:**
- `documentation.html` — update relevant sections

**Steps:**

1. **Read** the relevant sections of `documentation.html` to find:
   - Section covering Engine Lifecycle / Protocol Messages (likely Section 10 or 12)
   - Any existing mention of SetProperty/SetSecureProperty
   - Security notes about the named pipe transport

2. **Update** the Protocol & IPC section to document:
   - `SetPropertyMessage (0x0208)` — name + value string
   - `SetSecurePropertyMessage (0x0209)` — name + length + raw bytes
   - Phase gating: only during Initializing/Detecting/Planning

3. **Update** the Engine Architecture section to note:
   - MSI operations now use `MsiInstallProduct` / `MsiConfigureProduct` P/Invoke
   - No `msiexec.exe` process spawning for MSI packages
   - `IMsiApi` abstraction for testability

4. **Update** the Security section to document:
   - `SecureVariable` for pinned, zeroable secret storage
   - Property string zeroing after P/Invoke calls
   - No command-line exposure of any MSI properties

5. **Update** the Error Code Reference if any new error codes were introduced.

**Expected Outcome:** Documentation reflects the new protocol messages, P/Invoke execution model, and security properties.

---

## Task 11: Commit

**Steps:**

1. **Run code review** using `superpowers:code-reviewer` with two different models (Opus + Sonnet). Both must pass.

2. **Verify pre-commit quality gates:**
   - CLAUDE.md updated if structural changes occurred (new files in Protocol, new SecureVariable class)
   - OWASP ASVS check: secret handling, input validation on property names, pipe authentication
   - Security audit: no hardcoded secrets, SecureVariable properly zeroed, no command-line exposure
   - Performance review: no unnecessary allocations in hot paths, property string built once
   - `dotnet build` — 0 warnings
   - `dotnet test` — all pass

3. **Stage specific files** — do not use `git add -A`:

```bash
git add src/FalkForge.Platform.Windows/NativeMethods.Msi.cs  # or wherever P/Invoke landed
git add src/FalkForge.Engine.Protocol/Messages/SetPropertyMessage.cs
git add src/FalkForge.Engine.Protocol/Messages/SetSecurePropertyMessage.cs
git add src/FalkForge.Engine.Protocol/Messages/MessageType.cs
git add src/FalkForge.Engine.Protocol/Serialization/MessageSerializer.cs
git add src/FalkForge.Engine.Protocol/Serialization/MessageDeserializer.cs
git add src/FalkForge.Ui/EngineClient.cs
git add src/FalkForge.Engine/EngineHost.cs
git add src/FalkForge.Engine/Variables/VariableStore.cs
git add src/FalkForge.Engine/Variables/SecureVariable.cs
git add src/FalkForge.Engine/Planning/Planner.cs
git add src/FalkForge.Engine/Planning/PlanAction.cs
git add src/FalkForge.Engine/Execution/MsiExecutor.cs
git add src/FalkForge.Engine.Elevation/Commands/MsiInstallCommand.cs
git add src/FalkForge.Engine.Elevation/Commands/MsiUninstallCommand.cs
git add tests/  # all new and modified test files
git add documentation.html
git add CLAUDE.md  # if updated
```

4. **Commit:**

```bash
git commit -m "feat: wire SetProperty/SetSecureProperty through protocol with MsiInstallProduct P/Invoke"
```

**Expected Outcome:** Single clean commit on the `fix/security-memory-perf` branch (or a new feature branch if appropriate) with all changes, passing build, passing tests, and reviewed by two AI models.
