# Engine Lifecycle Hooks & Property Passing

## Goal

Give custom UI page developers hooks into the engine's Detect/Plan/Apply phases, plus the ability to pass properties (including secure ones) to MSI packages. Demonstrate everything in demo 14.

## Lifecycle Hooks API

Six virtual methods on `InstallerPage`. "Begin" hooks return `bool` (false = cancel). "Complete" hooks are observe-only.

```csharp
protected virtual Task<bool> OnDetectBeginAsync() => Task.FromResult(true);
protected virtual Task OnDetectCompleteAsync(DetectResult result) => Task.CompletedTask;
protected virtual Task<bool> OnPlanBeginAsync(InstallAction action) => Task.FromResult(true);
protected virtual Task OnPlanCompleteAsync(PlanResult result) => Task.CompletedTask;
protected virtual Task<bool> OnApplyBeginAsync() => Task.FromResult(true);
protected virtual Task OnApplyCompleteAsync(ApplyResult result) => Task.CompletedTask;
```

Called by `CustomShellViewModel.ExecuteEngineActionAsync` on the **current page**:

```
OnDetectBegin → DetectAsync → OnDetectComplete →
OnPlanBegin → PlanAsync → OnPlanComplete →
OnApplyBegin → ApplyAsync → OnApplyComplete
```

If any Begin hook returns false, execution stops, `IsApplying` resets, user stays on page.

## Property Passing API

```csharp
// On IInstallerEngine:
void SetProperty(string name, string value);
void SetSecureProperty(string name, SensitiveBytes value);
```

- `SetProperty`: Regular properties passed to MSI via command line (safe for non-sensitive values)
- `SetSecureProperty`: Secure properties transported via HMAC-authenticated named pipe to elevated process, which calls `MsiSetProperty()` directly on the MSI session handle. Never on command line, never in logs.

### Secure Property Flow

```
UI Page → SetSecureProperty("DBPASSWORD", bytes)
  → DPAPI-encrypted in engine memory
  → Sent via named pipe (HMAC-SHA256) to elevated process
  → Elevated process decrypts, calls MsiSetProperty(hInstall, name, value)
  → Memory zeroed immediately after MsiSetProperty returns
  → Property available to MSI custom actions within session
```

### New Protocol Message

```csharp
public sealed record SetSecurePropertyMessage(string Name, byte[] EncryptedValue);
```

## Demo 14: "Contoso DataHub" Enterprise Database App

4-page installer demonstrating all lifecycle hooks and property passing:

1. **WelcomePage** — Product intro
2. **ConfigPage** — DB server, name, integrated security toggle, password (PasswordBridge), install dir
3. **ProgressPage** — Overrides all 6 hooks, shows status log, passes properties via SetProperty/SetSecureProperty
4. **CompletePage** — Success summary

## Files Changed

### Framework (src/)
- `InstallerPage.cs` — 6 virtual hooks
- `CustomShellViewModel.cs` — Call hooks in ExecuteEngineActionAsync
- `IInstallerEngine.cs` — SetProperty + SetSecureProperty
- `NullInstallerEngine.cs` — No-op implementations
- `Engine.Protocol/Messages/SetSecurePropertyMessage.cs` — New message type
- `Engine.Protocol/Serialization/` — Serialize/deserialize
- `Engine/VariableStore.cs` — Store properties
- `Engine.Elevation/Commands/SetSecurePropertyCommand.cs` — Elevated handler

### Tests
- InstallerPage lifecycle hook tests
- Hook cancellation tests
- SetProperty/SetSecureProperty on NullEngine
- Protocol serialization tests

### Demo 14
- 4 pages, 4 views, Program.cs, csproj

### Documentation
- documentation.html Section 10 — lifecycle hooks subsection
- documentation.html Section 11 — property passing in engine docs
