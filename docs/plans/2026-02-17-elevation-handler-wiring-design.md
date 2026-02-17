# Elevation Handler Wiring

Wire the ElevatingHandler to launch the elevated companion process and route package execution commands through the elevation pipe.

## Architecture

### ElevationClient (Engine-Side Abstraction)

**`IElevationClient`** — engine's interface for talking to the elevated process:
- `SendCommandAsync(string commandName, byte[] payload, CancellationToken ct)` → `Task<Result<byte[]>>`
- Extends `IAsyncDisposable`

**`ElevationClient`** — pipe-based implementation:
- Assigns incrementing `SequenceId` to each request
- Registers `TaskCompletionSource<ElevateResultMessage>` keyed by ID
- Sends `ElevateExecuteMessage` via PipeServer
- Pipe receive callback matches `ElevateResultMessage` by SequenceId, completes TCS
- Configurable timeout (default 10 minutes for MSI operations)
- One request at a time (no pipelining — elevated process handles commands sequentially)
- Dispose sends disconnect signal and kills elevated process if still running

### IProcessLauncher (Testability)

**`IProcessLauncher`** — abstraction for spawning the elevated process:
- `Launch(string exePath, string arguments)` → `Result<Process>`

**`ProcessLauncher`** — production implementation:
- `ProcessStartInfo` with `Verb = "runas"`, `UseShellExecute = true`

### ElevatingHandler Implementation

Sequence:
1. Generate 32-byte random secret via `RandomNumberGenerator.Fill()`
2. Generate unique pipe name `falkforge_elev_{Guid:N}`
3. Create `PipeServer` with `PipeConnectionOptions(pipeName, secret)`
4. Locate `FalkForge.Engine.Elevation.exe` alongside engine EXE
5. Launch elevated via `IProcessLauncher` with `--pipe {name} --secret {base64} --parent-pid {pid}`
6. Wait for connection with 60-second timeout (covers UAC prompt)
7. HMAC handshake (built into PipeServer)
8. Create `ElevationClient`, store on `EngineContext.ElevationClient`
9. Store process handle on `EngineContext.ElevatedProcess`
10. Return `EnginePhase.Applying`

Error handling:
- UAC declined (`Win32Exception`) → `EnginePhase.Failed`, "Elevation was cancelled by the user"
- Companion not found → `EnginePhase.Failed`, "Elevation companion not found"
- Handshake timeout → kill process, `EnginePhase.Failed`, "Elevation handshake timed out"

### PackageExecutor Routing

`MsiExecutor` gets `IElevationClient?` parameter:
- If null → current behavior (spawn `msiexec.exe` directly)
- If present → serialize MSI path + args, call `SendCommandAsync("MsiInstall", payload)` or `"MsiUninstall"`, map result to `ExecutionOutcome`

Only MSI install/uninstall wired (matching existing elevated commands). Other executors unchanged.

### EngineContext Changes

New fields:
- `IElevationClient? ElevationClient`
- `Process? ElevatedProcess`

### Teardown

- `CompletingHandler`: dispose `ElevationClient`, wait 5s for process exit, then kill
- `ShutdownHandler`: same as safety net
- `EngineContext.Dispose()`: final safety net
- No new shutdown message — pipe disconnect is the signal (ElevatedHost already handles this)

## Files

| Action | File | Purpose |
|--------|------|---------|
| Create | `src/FalkForge.Engine/Elevation/IElevationClient.cs` | Engine-side abstraction |
| Create | `src/FalkForge.Engine/Elevation/ElevationClient.cs` | Pipe-based implementation |
| Create | `src/FalkForge.Engine/Elevation/IProcessLauncher.cs` | Process launch abstraction |
| Create | `src/FalkForge.Engine/Elevation/ProcessLauncher.cs` | Production implementation |
| Edit | `src/FalkForge.Engine/Phases/ElevatingHandler.cs` | Replace stub |
| Edit | `src/FalkForge.Engine/EngineContext.cs` | Add elevation fields |
| Edit | `src/FalkForge.Engine/Execution/MsiExecutor.cs` | Elevation routing |
| Edit | `src/FalkForge.Engine/Phases/CompletingHandler.cs` | Teardown |
| Edit | `src/FalkForge.Engine/Phases/ShutdownHandler.cs` | Teardown safety net |
| Create | `tests/FalkForge.Engine.Tests/Elevation/ElevationClientTests.cs` | ~8 tests |
| Create | `tests/FalkForge.Engine.Tests/Elevation/ElevatingHandlerTests.cs` | ~5 tests |
| Create | `tests/FalkForge.Engine.Tests/Elevation/MsiExecutorElevationTests.cs` | ~4 tests |
| Edit | `CLAUDE.md` | Document elevation wiring |

**4 new src files, 5 edited src files, 3 new test files, ~17 new tests.**

## Implementation Order

1. IElevationClient + IProcessLauncher interfaces
2. ElevationClient + ProcessLauncher implementations
3. EngineContext fields
4. ElevatingHandler replacement
5. MsiExecutor elevation routing
6. CompletingHandler + ShutdownHandler teardown
7. Tests (ElevationClient, ElevatingHandler, MsiExecutor)
8. CLAUDE.md update

## Security

- HMAC-SHA256 handshake prevents rogue pipe connections
- `PipeOptions.CurrentUserOnly` restricts pipe access
- Parent PID watchdog ensures elevated process dies if engine crashes
- Whitelisted commands only — elevated process rejects unknowns
- Each command validates inputs (regex, path traversal, denied prefixes)
- Random secret generated per session, never persisted
