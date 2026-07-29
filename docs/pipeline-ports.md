# FalkForge Installer Pipeline Ports

The Engine pipeline (`src/FalkForge.Engine/Pipeline/`) follows hexagonal architecture: an `IInstallerPipeline` orchestrator drives `*Step` handlers which talk to the outside world through narrow ports. Each port has at least one production adapter, and most have a test/null adapter or are easy to fake from the interface.

This document is the per-port reference for the ports introduced when the engine was split off from the legacy `EngineHost` / `EngineStateMachine` pair. It corresponds to the `Pipeline/` source folder and is a checklist for anyone adding, replacing, or testing an adapter.

## Pipeline Orchestrator

### `IInstallerPipeline`

Top-level coordinator for an installer run. Enforces phase ordering (`Detect → Plan → (Elevate) → Apply`) and delegates each phase to step implementations injected by `InstallerPipelineBuilder`. Returns `Result<Unit>` from every phase rather than throwing, so callers can distinguish user cancellation, precondition failure, and infrastructure errors without exception handling.

**Lifecycle:** `IAsyncDisposable`. The pipeline goes through an internal phase enum `Initial → Detected → Planned → Elevated → Applied`. Calling a phase out of order returns `ErrorKind.EngineError`. Re-detect is allowed (Detect → Detect), but Plan/Apply cannot be rolled backward.

**Apply failure path:** if `ApplyAsync` fails and a rollback step is configured, the pipeline runs rollback synchronously before propagating the error.

**Contract:**

```csharp
public interface IInstallerPipeline : IAsyncDisposable
{
    Task<Result<Unit>> DetectAsync(CancellationToken ct);
    Task<Result<Unit>> PlanAsync(UiRequest.Plan request, CancellationToken ct);
    Task<Result<Unit>> ElevateAsync(CancellationToken ct);
    Task<Result<Unit>> ApplyAsync(CancellationToken ct);
    Result<Unit> ExportPlan(string? outputPath);
}
```

Production implementation: `InstallerPipeline` (internal). Build via `InstallerPipelineBuilder`. The orchestrator is driven by `PipelineRunner`, which reads `UiRequest` events from an `IUiChannel` and invokes the matching phase methods. `Elevate` runs automatically between Plan and Apply; it is a no-op when no `IElevatedCommandGateway` was registered.

## Steps

All step interfaces are `internal` and consumed only by `InstallerPipeline`. They share a common shape: `ExecuteAsync(PipelineContext ctx, ...)` returning `Task<Result<Unit>>`. State flows through the mutable `PipelineContext` bag.

### `IDetectStep` — `DetectStep`
- **Role:** Load manifest, run `PackageDetector` + dependency detection, optionally check the update feed.
- **Inputs:** `InstallerManifest`, `IRegistry`, optional `UpdateChecker`.
- **Outputs:** `ctx.Manifest`, `ctx.Detection`, `ctx.RelatedBundles`, `ctx.AvailableUpdate`.
- **Ports used:** `IUiChannel` (PhaseChanged + Log + UpdateAvailable events).

### `IPlanStep` — `PlanStep`
- **Role:** Run `Planner.CreatePlan`, gate on license acceptance, expand secret-bracket properties.
- **Inputs:** `Planner`, optional `VariableStore`, the `UiRequest.Plan` request (action + install dir + feature selections + properties + secure properties + license-accepted flag).
- **Outputs:** `ctx.Plan`, `ctx.PlanRequest`.
- **Ports used:** `IUiChannel` (PhaseChanged + Log).

### `IElevateStep` — `ElevateStep`
- **Role:** Stand up the elevated companion process before Apply.
- **Inputs:** `IElevatedCommandGateway`.
- **Outputs:** `ctx.ElevationGateway` is set on success.
- **Ports used:** `IElevatedCommandGateway.StartAsync`, `IUiChannel` (PhaseChanged + Log).

### `IApplyStep` — `ApplyStep`
- **Role:** Execute each `PlanAction` via `PackageExecutor`, journal each installed package, optionally orchestrate Restart Manager.
- **Inputs:** `PackageExecutor`, `IRollbackJournalStore`, optionally `ctx.RestartManager` and `ctx.IsDryRun`.
- **Outputs:** `ctx.RebootRequired`, journal entries for each successful install.
- **Ports used:** `IRollbackJournalStore.Append`, `IUiChannel` (PhaseChanged + Progress + Log).

### `IRollbackStep` — `RollbackStep`
- **Role:** Replay undo operations in reverse order, then clear the journal.
- **Inputs:** `IRollbackJournalStore`, `IReadOnlyList<IUndoOperation>`, optional `IFalkLogger`.
- **Outputs:** Empty journal on success.
- **Ports used:** `IRollbackJournalStore.LoadAll` / `Clear`, `IUiChannel` (PhaseChanged + RollbackStep + Log).

## Ports

### `IUiChannel`

**Role:** Cross-process UI communication. Pipeline code emits `PipelineEvent` and reads `UiRequest`; the channel hides binary message framing, the HMAC pipe handshake, and the wire-level `EngineMessage` subtypes.

**Contract:**

```csharp
public interface IUiChannel : IAsyncDisposable
{
    void SetSessionCorrelationId(Guid id);
    Task SendAsync(PipelineEvent evt, CancellationToken ct);
    IAsyncEnumerable<UiRequest> ReadRequestsAsync(CancellationToken ct);
}
```

**Threading:** `SendAsync` is async and may be called from any thread; production adapter serializes through the underlying `PipeServer`. `ReadRequestsAsync` returns an `IAsyncEnumerable` backed by an unbounded channel — single-reader recommended (the channel is configured `SingleWriter = true, SingleReader = false` but callers normally consume from one loop). `SetSessionCorrelationId` is called once at session start before any other call; the field is `volatile`.

**Lifecycle:** `IAsyncDisposable`. Disposing completes the request channel writer and disposes the underlying pipe.

**Adapters:**
- `NamedPipeUiChannel` — bridges a `PipeServer` to the pipeline contract. Translates `PipelineEvent` → `EngineMessage` (PhaseChanged, Progress, Log, Failed, RollbackStep-as-Log, UpdateAvailable). Stamps the session correlation id on outbound `LogMessage` and `PhaseChangedMessage` frames so on-disk logs and wire frames share an id. Accumulates pre-plan state from inbound messages (`SetInstallDirectory`, `SetFeatureSelection`, `LicenseMessage`, `SetPropertyMessage`, `SetSecurePropertyMessage`) and bundles it into the `UiRequest.Plan` emitted when `RequestPlanMessage` arrives. Property names are validated via `PropertyNameValidator` before being accepted.
- `NullUiChannel` (internal) — singleton no-op channel used when the pipeline runs headless. Drops outbound events; `ReadRequestsAsync` yields immediately. Used as the default when `InstallerPipelineBuilder.WithUiChannel` is not called.
- `NamedPipeUiChannel.CreateNullChannel()` — variant that wraps no `PipeServer` but exposes the same type. Used in CLI / test scenarios where downstream code expects a `NamedPipeUiChannel` specifically.

**Test adapter:** `NullUiChannel` works for ordering tests. For assertion tests, fakes typically implement `IUiChannel` directly and capture `PipelineEvent`s into a list.

---

### `IElevatedCommandGateway`

**Role:** Cross-process elevation. Hides HMAC handshake, PID + start-time verification, the elevated companion process spawn, and pipe framing.

**Contract:**

```csharp
public interface IElevatedCommandGateway : IAsyncDisposable
{
    Task<Result<Unit>> StartAsync(CancellationToken ct);
    Task<Result<byte[]>> SendCommandAsync(
        string commandName,
        byte[] payload,
        IProgress<int>? progress,
        CancellationToken ct);
}
```

**Threading:** `StartAsync` must be called once before any `SendCommandAsync` call. Production adapter serializes commands through `ElevationClient`. `progress` reports `[0..100]` percent during long-running commands such as MSI installs.

**Lifecycle:** `IAsyncDisposable`. Disposing kills the companion process tree (best-effort) and tears down the pipe. Once disposed, further calls return `ErrorKind.ElevationError`.

**Error model:** All failure paths surface `ErrorKind.ElevationError` with a human-readable reason. `StartAsync` enforces a 60-second timeout for both the secret-pipe handshake and the main pipe connect.

**Adapters:**
- `NamedPipeElevationGateway` — production adapter. Generates a 32-byte HMAC secret with `RandomNumberGenerator.Fill`, delivers it to the companion through a one-shot init pipe (never via CLI args), then waits for the companion to connect on the main pipe. CLI args carry only pipe names and the parent PID. Disposing kills the companion process tree.

**Test adapter:** None shipped. Fakes implement `IElevatedCommandGateway` directly and either reply with canned bytes or invoke an in-process command dispatcher.

---

### `IRollbackJournalStore`

**Role:** Durable storage for the rollback journal. Hides on-disk format and flush semantics.

**Contract:**

```csharp
public interface IRollbackJournalStore : IDisposable
{
    Result<Unit> Append(JournalEntry entry);
    Result<IReadOnlyList<JournalEntry>> LoadAll();
    Result<Unit> Clear();
}
```

**Threading:** Synchronous. `Append` must flush to durable storage before returning, so a process crash after a successful append leaves the entry readable by `LoadAll`. Concurrency contract is not stated explicitly; production adapter is not documented as thread-safe and callers should serialize.

**Lifecycle:** `IDisposable`. Disposing closes the underlying file handle.

**Adapters:**
- `FileSystemJournalStore` — wraps `RollbackJournal`, which writes with `FileOptions.WriteThrough`. `LoadAll` returns the in-memory entry list accumulated since construction or last `Clear`. `Clear` disposes the journal, deletes the file, and reopens a fresh one at the same path. Constructor throws `InvalidOperationException` if the file cannot be opened.

**Test adapter:** None shipped. Fakes typically maintain a `List<JournalEntry>` and return it from `LoadAll`.

---

### `ISystemClock`

**Role:** Abstracts wall-clock access so tests can supply a deterministic fake instead of `DateTimeOffset.UtcNow`.

**Contract:**

```csharp
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
```

**Threading:** Trivially thread-safe; property read only.

**Adapters:**
- `SystemClock` — delegates to `DateTimeOffset.UtcNow`.

**Test adapter:** None shipped. Fakes are typically a one-line record `sealed record FakeClock(DateTimeOffset UtcNow) : ISystemClock`.

---

## Adapter Disposal & Lifetime

| Adapter | Disposal | Notes |
|---------|----------|-------|
| `InstallerPipeline` | `IAsyncDisposable` | Disposing flips an internal `_disposed` flag; later phase calls return `ErrorKind.EngineError`. Steps and ports are **not** disposed by the pipeline — the builder's caller owns their lifetime. |
| `NamedPipeUiChannel` | `IAsyncDisposable` | Completes the request channel writer and disposes the wrapped `PipeServer`. |
| `NullUiChannel` | `IAsyncDisposable` | No-op (singleton). |
| `NamedPipeElevationGateway` | `IAsyncDisposable` | Best-effort `Process.Kill(entireProcessTree: true)` on the companion, disposes `ElevationClient` and the pipe. Idempotent. |
| `FileSystemJournalStore` | `IDisposable` | Closes the journal file handle. |
| `SystemClock` | None | Stateless. |

The pipeline orchestrator and `PipelineRunner` do not own port lifetimes. The composition root (typically `EngineSession` or a CLI entry point) constructs each adapter, hands it to `InstallerPipelineBuilder`, and disposes it after `RunAsync` returns.

## Builder Wiring Summary

`InstallerPipelineBuilder` accepts the following `With…` calls. Steps are wired only when their required components are present, otherwise the corresponding phase passes through without executing step logic — useful for ordering-only tests.

| Builder method | Required for |
|----------------|--------------|
| `WithManifest(InstallerManifest)` | `DetectStep`, `PlanStep` |
| `WithRegistry(IRegistry)` | `DetectStep` |
| `WithVariableStore(VariableStore)` | `PlanStep` (optional — secret-bracket expansion) |
| `WithPackageExecutor(PackageExecutor)` | `ApplyStep` |
| `WithJournalStore(IRollbackJournalStore)` | `ApplyStep`, `RollbackStep` |
| `WithUndoOperations(IReadOnlyList<IUndoOperation>)` | `RollbackStep` (no-op when omitted) |
| `WithElevationGateway(IElevatedCommandGateway)` | `ElevateStep` (skipped when omitted) |
| `WithUiChannel(IUiChannel)` | All steps (defaults to `NullUiChannel.Instance`) |
| `WithLogger(IFalkLogger)` | `RollbackStep` diagnostics |
| `WithTrustStoreAdvanceOnVerifiedApply(bool = true)` | `ApplyStep` (require-signed update path only — forwards the manifest signature's epoch + revocations to the elevated companion after a successful apply; a fresh install never advances the store) |
| `WithPayloadRoot(string)` | `ApplyStep` (self-extract path — resolves each action's install path under `{payloadRoot}/{PackageId}` with a containment guard, instead of the manifest's build-machine `SourcePath`) |

Two further methods are `internal`, for in-assembly composition only, and are therefore not part of
the public builder surface: `WithUpdateServices(UpdateChecker, UpdateService)` (wires the manifest's
update feed into `DetectStep` and `IInstallerPipeline.LaunchUpdate`) and
`WithIntegrityTrustPolicy(TrustPolicy)` (overrides the apply-time integrity gate's trust policy).

`ISystemClock` has a production adapter (`SystemClock`) but no `InstallerPipelineBuilder.With…`
method — the builder does not accept it today, and no phase step consumes it. Wiring it in is a real
design task (a step needs to be written that calls it), not a follow-up rename.

An earlier `IPayloadCache` / `IPayloadSource` / `ILayoutStore` / `IRandomSource` port set was
built alongside these but never wired to any step, and was removed rather than left as scaffolding.
The live payload path is `PayloadDownloader` → `PackageCache` → `LayoutManager`; see
`src/FalkForge.Engine/Download/`, `src/FalkForge.Engine/Cache/`, and `src/FalkForge.Engine/Layout/`.

## See Also

- `src/FalkForge.Engine/Pipeline/` — source folder for every type referenced in this document.
- `CLAUDE.md` — "Engine Architecture (3-process model)" section for the UI ↔ Engine ↔ Elevated process layout.
- `src/FalkForge.Engine.Protocol/Messages/` — the wire-level `EngineMessage` types that `NamedPipeUiChannel` translates.
- `src/FalkForge.Engine/Journal/` — `JournalEntry`, `RollbackJournal`, and the undo operation hierarchy consumed by `IRollbackJournalStore`.
- `src/FalkForge.Engine/Cache/` — `CacheLayout`, the path-traversal-hardened cache layout, and `PackageCache`.
- `src/FalkForge.Engine/Download/` — `PayloadDownloader`, `TokenBucket`, retry policy, and `UpdateChecker`.
