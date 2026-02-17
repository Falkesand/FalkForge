# Bundle-Level Variables

Author-defined variables with types, defaults, persistence, and secure secret handling for conditional installation flows.

## Variable Model

`BundleVariableModel` record:
- `Name` (string) — variable identifier
- `Type` (BundleVariableType) — String, Numeric, Version
- `DefaultValue` (string?) — initial value
- `Persisted` (bool) — save to registry across reboots
- `Hidden` (bool) — redact from logs
- `Secret` (bool) — never persist, secure memory, implies Hidden

## Builder API

```csharp
builder.Variable("INSTALLFOLDER", v => v.String().Default(@"C:\Program Files\MyApp"));
builder.Variable("INSTALLDB", v => v.String().Default("false").Persisted());
builder.Variable("DB_SERVER", v => v.String().Hidden());
builder.Variable("DB_PASSWORD", v => v.String().Secret());
builder.Variable("RETRY_COUNT", v => v.Numeric().Default("3"));
```

## Manifest Transmission

`ManifestVariable` record in Engine.Protocol/Manifest:
- Name, Type ("string"/"numeric"/"version"), DefaultValue, Persisted, Hidden, Secret
- String Type field for forward-compatibility (no Compiler.Bundle dependency)
- Added to InstallerManifest.Variables array
- AOT-safe via ManifestJsonContext

## Engine Runtime

### Variable Seeding
1. `BuiltInVariables.Populate()` — system variables (30+)
2. Iterate `manifest.Variables` — set defaults on VariableStore
3. `VariablePersistence.LoadPersistedVariables()` — override defaults with registry values

### Secret Handling
`SecureVariable` class:
- Stores value in pinned `byte[]` (UTF-8)
- `Dispose()` zeros via `CryptographicOperations.ZeroMemory()`
- GCHandle pin prevents GC relocation

`VariableStore` changes:
- `ConcurrentDictionary<string, SecureVariable> _secrets`
- `SetSecret(name, value)` — wraps in SecureVariable, disposes previous
- `GetSecret(name)` → `Result<string>` — temporary string
- `IsSecret(name)` → bool — for logger redaction
- `DisposeSecrets()` — zeros all secrets
- Implements `IDisposable`

### Registry Persistence
`VariablePersistence` class:
- Key: `HKLM\SOFTWARE\FalkForge\Burn\{BundleId}\Variables` (PerMachine) or HKCU (PerUser)
- `LoadPersistedVariables()` — reads only variables marked Persisted=true
- `SavePersistedVariables()` — writes persisted variables at end of Apply
- `ClearPersistedVariables()` — deletes key on uninstall
- Uses IRegistry platform abstraction (no direct Microsoft.Win32)

### Condition Evaluation
`ConditionEvaluator.ResolveVariable()` checks secrets too. Value retrieved temporarily for evaluation, then discarded.

### MSI Property Passing
`MsiExecutor` retrieves secret values via `GetSecret()` for command-line passing. String is short-lived.

## Validation

- BDL010: Variable name empty
- BDL011: Duplicate variable name
- BDL012: Default value doesn't match declared type
- BDL013: Secret variable cannot be marked Persisted

## WiX Decompiler

`WixManifestMapper` maps `<Variable>` elements to `BundleVariableModel` (currently unmapped).
`BundleCSharpEmitter` emits `builder.Variable(...)` calls.
FALKBUNDLE `ManifestMapper` maps `InstallerManifest.Variables` to `BundleModel.Variables`.

## Files

| Action | File | Purpose |
|--------|------|---------|
| Create | `src/FalkForge.Compiler.Bundle/BundleVariableModel.cs` | Variable record |
| Create | `src/FalkForge.Compiler.Bundle/BundleVariableType.cs` | Type enum |
| Create | `src/FalkForge.Compiler.Bundle/Builders/BundleVariableBuilder.cs` | Fluent builder |
| Create | `src/FalkForge.Engine.Protocol/Manifest/ManifestVariable.cs` | IPC record |
| Create | `src/FalkForge.Engine/Variables/SecureVariable.cs` | Pinned secret storage |
| Create | `src/FalkForge.Engine/Variables/VariablePersistence.cs` | Registry persistence |
| Edit | `src/FalkForge.Compiler.Bundle/BundleModel.cs` | Variables field |
| Edit | `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs` | Variable() method |
| Edit | `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs` | Serialize |
| Edit | `src/FalkForge.Compiler.Bundle/Compilation/ManifestJsonContext.cs` | AOT |
| Edit | `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` | BDL010-013 |
| Edit | `src/FalkForge.Engine.Protocol/Manifest/InstallerManifest.cs` | Variables field |
| Edit | `src/FalkForge.Engine/Variables/VariableStore.cs` | Secrets + IDisposable |
| Edit | `src/FalkForge.Engine/EngineHost.cs` | Seed + load |
| Edit | `src/FalkForge.Engine/Phases/CompletingHandler.cs` | Save + dispose |
| Edit | `src/FalkForge.Engine/Execution/MsiExecutor.cs` | Secret properties |
| Edit | `src/FalkForge.Decompiler/WixManifestMapper.cs` | Map variables |
| Edit | `src/FalkForge.Decompiler/BundleCSharpEmitter.cs` | Emit variables |
| Edit | `src/FalkForge.Decompiler/ManifestMapper.cs` | Map manifest vars |

**6 new src, 13 edited src, 6 new test files, ~38 new tests.**

## Implementation Order

1. Model + enum + builder + validation (Compiler.Bundle)
2. ManifestVariable + InstallerManifest + ManifestGenerator + JsonContext (Protocol)
3. SecureVariable + VariableStore changes (Engine)
4. VariablePersistence (Engine)
5. EngineHost seeding + CompletingHandler persistence (Engine)
6. MsiExecutor secret passing (Engine)
7. Decompiler mappings (Decompiler)
8. Tests throughout
9. CLAUDE.md update
