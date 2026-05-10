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
- **Inputs:** `IRollbackJournalStore`, `IReadOnlyList<IUndoOperation>`, optional `IEngineLogger`.
- **Outputs:** Empty journal on success.
- **Ports used:** `IRollbackJournalStore.LoadAll` / `Clear`, `IUiChannel` (PhaseChanged + RollbackStep + Log).

## Ports

### `IPayloadSource`

**Role:** Network I/O for downloading installer payloads.

**Contract:**

```csharp
public interface IPayloadSource
{
    Task<Result<string>> DownloadAsync(
        string url,
        string expectedSha256,
        string destinationPath,
        IProgress<(long BytesReceived, long TotalBytes)>? progress,
        CancellationToken ct);
}
```

**Threading:** Async; concurrent calls allowed, the production adapter uses a shared `HttpClient` and an optional shared `TokenBucket`. Implementations should be thread-safe.

**Adapters:**
- `HttpPayloadSource` — wraps `PayloadDownloader`. Inherits HTTPS enforcement, three-attempt retry with exponential back-off, SHA-256 verification, path-traversal guard on the destination path, optional bandwidth throttling via `TokenBucket`. `allowResume: false` is hard-coded; consumers that need resume must call `PayloadDownloader` directly.

**Test adapter:** None shipped. Easy to fake by writing a dummy file at `destinationPath` and returning `Result<string>.Success(destinationPath)`.

---

### `IPayloadCache`

**Role:** Local disk cache for downloaded installer payloads, keyed by `(bundleId, packageId, sha256)`.

**Contract:**

```csharp
public interface IPayloadCache
{
    Result<string> Store(Guid bundleId, string packageId, string sha256, string sourceFilePath);
    Result<string> Resolve(Guid bundleId, string packageId, string sha256);
    Result<Unit>   Remove(Guid bundleId, string packageId, string sha256);
}
```

**Threading:** Synchronous. `Resolve` and `Remove` perform directory enumeration and SHA-256 hashing of every file in the package directory, so callers should treat them as I/O-bound. Production adapter has no internal locking — concurrent `Store` calls for the same key are racy at the file-copy level.

**Error model:** `ErrorKind.CacheError` for hash mismatch, I/O failure, or path-traversal rejection; `ErrorKind.FileNotFound` from `Resolve` when no entry exists.

**Adapters:**
- `DiskPayloadCache` — backs onto `CacheLayout`. Inherits the three-layer path-traversal defense: allowlist regex on `packageId`, `Path.GetFileName` sanitization on file name, `Path.GetFullPath` containment check. Hash computed via `SHA256.HashData(Stream)` (no allocation). Partial files on hash mismatch are deleted.

**Test adapter:** None shipped. Trivial to fake with an in-memory `Dictionary<(Guid, string, string), string>`.

---

### `ILayoutStore`

**Role:** Persist / load the `InstallerManifest` for installer layout mode (admin image / unattended source).

**Contract:**

```csharp
public interface ILayoutStore
{
    Task<Result<Unit>> WriteAsync(InstallerManifest manifest, string layoutPath, CancellationToken ct);
    Task<Result<InstallerManifest>> ReadAsync(string layoutPath, CancellationToken ct);
}
```

**Threading:** Async file I/O. Concurrent `WriteAsync` to the same `layoutPath` is undefined.

**Error model:** `ErrorKind.LayoutError` for serialization or I/O failure, `ErrorKind.FileNotFound` from `ReadAsync` when `manifest.json` is absent.

**Adapters:**
- `FileSystemLayoutStore` — writes `manifest.json` inside `layoutPath` using the AOT-safe `LayoutJsonContext`. Creates the directory on `WriteAsync`. Returns a typed failure when JSON deserialization yields null.

**Test adapter:** None shipped. Trivial to fake with an in-memory dictionary keyed by `layoutPath`.

---

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

### `IRandomSource`

**Role:** Abstracts entropy generation so tests can supply a deterministic seed instead of cryptographic randomness and `Guid.NewGuid()`.

**Contract:**

```csharp
public interface IRandomSource
{
    Guid NewGuid();
    void Fill(Span<byte> buffer);
}
```

**Threading:** Production adapter delegates to `RandomNumberGenerator.Fill` and `Guid.NewGuid`, both thread-safe. `Fill` allocates nothing (Span overload).

**Adapters:**
- `CryptoRandomSource` — production adapter. Cryptographically strong; suitable for nonce and HMAC-secret generation. `Fill` short-circuits on an empty span.

**Test adapter:** None shipped. Fakes typically wrap a seeded `Random` for reproducible tests; do not use such fakes in production code paths that need cryptographic strength.

---

## Adapter Disposal & Lifetime

| Adapter | Disposal | Notes |
|---------|----------|-------|
| `InstallerPipeline` | `IAsyncDisposable` | Disposing flips an internal `_disposed` flag; later phase calls return `ErrorKind.EngineError`. Steps and ports are **not** disposed by the pipeline — the builder's caller owns their lifetime. |
| `NamedPipeUiChannel` | `IAsyncDisposable` | Completes the request channel writer and disposes the wrapped `PipeServer`. |
| `NullUiChannel` | `IAsyncDisposable` | No-op (singleton). |
| `NamedPipeElevationGateway` | `IAsyncDisposable` | Best-effort `Process.Kill(entireProcessTree: true)` on the companion, disposes `ElevationClient` and the pipe. Idempotent. |
| `FileSystemJournalStore` | `IDisposable` | Closes the journal file handle. |
| `HttpPayloadSource` | None | The shared `HttpClient` and `TokenBucket` are owned by the caller. |
| `DiskPayloadCache` | None | Pure file I/O; nothing to release. |
| `FileSystemLayoutStore` | None | Pure file I/O. |
| `SystemClock`, `CryptoRandomSource` | None | Stateless. |

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
| `WithLogger(IEngineLogger)` | `RollbackStep` diagnostics |
| `WithClock(ISystemClock)` | Reserved — wired into download/cache/elevation steps in a follow-up |
| `WithRandom(IRandomSource)` | Reserved — wired into download/cache/elevation steps in a follow-up |
| `WithPayloadCache(IPayloadCache)` | Reserved |
| `WithPayloadSource(IPayloadSource)` | Reserved |
| `WithLayoutStore(ILayoutStore)` | Reserved |

Reserved ports are accepted by the builder today but not yet consumed inside the pipeline — they are queued for the wiring pass that retires the legacy `EngineHost`. This matches the in-tree `S4487` suppressions in `InstallerPipelineBuilder`.

## See Also

- `src/FalkForge.Engine/Pipeline/` — source folder for every type referenced in this document.
- `CLAUDE.md` — "Engine Architecture (3-process model)" section for the UI ↔ Engine ↔ Elevated process layout.
- `src/FalkForge.Engine.Protocol/Messages/` — the wire-level `EngineMessage` types that `NamedPipeUiChannel` translates.
- `src/FalkForge.Engine/Journal/` — `JournalEntry`, `RollbackJournal`, and the undo operation hierarchy consumed by `IRollbackJournalStore`.
- `src/FalkForge.Engine/Cache/` — `CacheLayout`, the path-traversal-hardened layout that `DiskPayloadCache` builds on.
- `src/FalkForge.Engine/Download/` — `PayloadDownloader`, `TokenBucket`, retry policy, and `UpdateChecker`.
