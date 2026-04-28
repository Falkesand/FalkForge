# RFC: Deepen the Engine phase pipeline

**Status:** Design accepted, implementation plan pending
**Author:** architectural review, 2026-04-11
**Scope:** `src/FalkForge.Engine/`, `src/FalkForge.Engine.Protocol/`, `tests/FalkForge.Engine.Tests/`

## Problem

The non-elevated engine process implements the install/repair/uninstall lifecycle as eleven `IEnginePhaseHandler` implementations (Initializing, Detecting, Planning, Elevating, Applying, Completing, RollingBack, Failed, Shutdown, plus glue phases) routed by a seventy-three-line `EngineStateMachine` switch-on-enum dispatcher. All eleven handlers read from and write to a single `EngineContext` mutable bag holding over thirty properties (`CurrentPhase`, `DetectedState`, `CurrentPlan`, `Variables`, `RebootRequired`, `ErrorMessage`, `FailedSegmentIndex`, `UserProperties`, `SecretPropertyNames`, and twenty-plus more). The preconditions between phases are implicit: `DetectingHandler` must populate `DetectedState` before `PlanningHandler` can read it, but nothing enforces this contract — a buggy or out-of-order call surfaces as a null reference.

Cross-process boundaries bleed into the phase code. `ApplyingHandler` is four hundred and ninety lines long and directly owns action-loop orchestration, rollback journaling, elevation IPC framing, progress emission over the UI pipe, and error classification. Handlers import the UI pipe server, the elevated command client, the rollback journal, the package executor, the process runner, the restart manager, the variable store, and the payload downloader. Every test that exercises a phase must build the entire `EngineContext`, stub or mock every helper on every dependency, and assert against bag mutations rather than observable outcomes. There are five phase-specific test files covering six hundred and forty-one lines, but none of them isolate a single phase — they all run inside a fully-instantiated state machine with mocked helpers.

The integration risk is concentrated in the seams between handlers, not inside any one handler's logic. "What happens when Detecting completes successfully but Planning is never called?" is not answered by any test. "What happens when Apply fails mid-package and the elevated process crashes during rollback?" is not answered by any test. The bag-passing architecture makes it impossible to assert these scenarios without constructing the entire real engine.

Navigability is equally bad. Answering "why does the apply rollback flow not cancel a pending download?" requires reading `ApplyingHandler.cs`, `PackageExecutor.cs`, `RollbackExecutor.cs`, `RollbackJournal.cs`, and at least three undo-operation files — seven file hops for one question.

This is a shallow-modules problem: eleven small files that individually do very little, a monolithic shared-state bag they all manipulate, and no narrow public interface to the cluster. Deepening it will produce one module that owns the entire install lifecycle as a testable unit and hides the state machine, the bag, the phase handler proliferation, the journal persistence format, and the IPC framing from every caller.

## Proposed Interface

The design splits the public surface into a facade for the ninety-five percent case (the production `EngineHost`) and an internal pipeline contract for tests, headless CLI drivers, CI dry-runs, and future service-mode hosts.

### Public facade — the ninety-five percent case

```csharp
namespace FalkForge.Engine;

/// <summary>
/// Binds the engine pipeline to a named-pipe transport and runs it until the UI
/// requests shutdown, the pipe closes, or the cancellation token fires. Wraps
/// every cross-boundary dependency in a production adapter, constructs the
/// internal pipeline, and pumps UI requests into it.
/// </summary>
public sealed class EngineSession : IAsyncDisposable
{
    public static EngineSession BindToPipe(
        string pipeName,
        string manifestPath,
        EngineSessionOptions? options = null);

    public Task<EngineOutcome> RunUntilShutdown(CancellationToken ct = default);

    public ValueTask DisposeAsync();
}

public sealed record EngineSessionOptions
{
    public IEngineLogger? Logger { get; init; }
    public string? LogDirectory { get; init; }
    public PipeConnectionOptions? PipeOptions { get; init; }
    public TimeSpan? HandshakeTimeout { get; init; }
    public bool WriteJournal { get; init; } = true;
}

public readonly record struct EngineOutcome(
    EngineTerminalState State,
    Error? Error,
    RollbackSummary? Rollback,
    TimeSpan Duration,
    IReadOnlyList<string> LogFiles);

public enum EngineTerminalState { Completed, Cancelled, RolledBack, Failed }

public readonly record struct RollbackSummary(
    int StepsExecuted,
    int StepsFailed,
    IReadOnlyList<RollbackStepResult> Steps);

public readonly record struct RollbackStepResult(
    string OperationKind,
    string Target,
    bool Succeeded,
    Error? Error);
```

Real `EngineHost.Main()` collapses to four meaningful lines:

```csharp
internal static async Task<int> Main(string[] args)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    await using var session = EngineSession.BindToPipe(args[0], args[1]);
    var outcome = await session.RunUntilShutdown(cts.Token);

    return outcome.State switch
    {
        EngineTerminalState.Completed => 0,
        EngineTerminalState.Cancelled => 2,
        EngineTerminalState.RolledBack => 3,
        EngineTerminalState.Failed => 1,
        _ => 1,
    };
}
```

### Internal pipeline contract — the five percent case

Tests, headless CLI callers, CI dry-run tooling, and future hosts bypass the pipe facade and drive `IInstallerPipeline` directly through `InstallerPipelineBuilder`.

```csharp
namespace FalkForge.Engine.Pipeline;

public interface IInstallerPipeline : IAsyncDisposable
{
    EnginePhase CurrentPhase { get; }
    IObservable<PipelineEvent> Events { get; }

    Task<Result<Unit>> StartAsync(InstallerManifest manifest, CancellationToken ct);
    Task<Result<DetectionReport>> DetectAsync(CancellationToken ct);
    Task<Result<InstallPlan>> PlanAsync(PlanRequest request, CancellationToken ct);
    Task<Result<ApplyReport>> ApplyAsync(IProgress<ApplyProgress>? progress, CancellationToken ct);
    ValueTask CancelAsync();
}

public readonly record struct PlanRequest(
    IReadOnlyDictionary<string, bool> FeatureSelections,
    string? InstallDirectory,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyDictionary<string, SensitiveBytes> SecureProperties,
    InstallAction Action);

public readonly record struct DetectionReport(
    InstallState State,
    string? CurrentVersion,
    IReadOnlyList<FeatureState> Features,
    IReadOnlyList<DependencyBlocker> Blockers);

public readonly record struct ApplyReport(int ExitCode, bool RebootRequired, bool RebootPending);

public abstract record PipelineEvent
{
    public sealed record PhaseChanged(EnginePhase Phase) : PipelineEvent;
    public sealed record Progress(int Percent, string? Message) : PipelineEvent;
    public sealed record Log(LogLevel Level, string Message) : PipelineEvent;
    public sealed record Failed(ErrorKind Kind, string Message) : PipelineEvent;
    public sealed record RollbackStep(RollbackStepResult Step) : PipelineEvent;
}

public sealed class InstallerPipelineBuilder
{
    public InstallerPipelineBuilder WithElevationGateway(IElevatedCommandGateway gateway);
    public InstallerPipelineBuilder WithUiChannel(IUiChannel channel);
    public InstallerPipelineBuilder WithPayloadSource(IPayloadSource source);
    public InstallerPipelineBuilder WithRollbackJournal(IRollbackJournalStore store);
    public InstallerPipelineBuilder WithPayloadCache(IPayloadCache cache);
    public InstallerPipelineBuilder WithLayoutStore(ILayoutStore store);
    public InstallerPipelineBuilder WithFileSystem(IFileSystem fs);
    public InstallerPipelineBuilder WithMsiApi(IMsiApi msi);
    public InstallerPipelineBuilder WithProcessRunner(IProcessRunner runner);
    public InstallerPipelineBuilder WithRestartManager(IRestartManager rm);
    public InstallerPipelineBuilder WithLogger(IEngineLogger logger);
    public InstallerPipelineBuilder WithClock(ISystemClock clock);
    public InstallerPipelineBuilder WithRandom(IRandomSource random);

    public IInstallerPipeline Build();
}

public static class InstallerPipelineBuilderDefaults
{
    public static InstallerPipelineBuilder WithWindowsProductionDefaults(
        this InstallerPipelineBuilder builder,
        EngineSessionOptions options);
}
```

### What the deepened module owns

Inside the pipeline core (internal, not part of any public surface):

- The phase state machine, transition table, and all transition-legality enforcement.
- The nine phase steps (Initializing, Detecting, Planning, Elevating, Applying, Completing, RollingBack, Failed, Shutdown). Each is a private sealed class implementing an internal `IPhaseStep`.
- The session state record that replaces the mutable `EngineContext` bag. Each phase step returns a typed `PhaseOutcome` union (`Continue`, `WaitForUiRequest`, `GoToFailed`) instead of mutating shared fields.
- The variable store, secure variable lifecycle (DPAPI wrap, zeroing on dispose), built-in variables, property name validation.
- Rollback routing: any phase-step failure triggers the `Failed → RollingBack → Shutdown` chain automatically. Callers never invoke rollback directly.
- UI-gated pause semantics: `DetectAsync`, `PlanAsync`, and `ApplyAsync` complete only when the corresponding phase step signals readiness for the next UI request.
- Crash-recovery journal replay: `InstallerPipelineBuilder.Build()` checks the rollback journal and resumes an in-flight rollback synchronously before returning a running pipeline.

### What the deepened module hides

The following concepts disappear from the public surface entirely:

- `IEnginePhaseHandler` and all eleven handler classes become internal `IPhaseStep` implementations.
- `EngineStateMachine` becomes an internal transition table driver.
- `EngineContext` is deleted. Its fields split into typed session state plus per-phase-step inputs.
- `EnginePhase` enum stays internal except where leaked through `IInstallerPipeline.CurrentPhase` and `PipelineEvent.PhaseChanged` for test assertions.
- The entire rollback journal persistence format, all undo operation types, and the journal file layout stay internal behind `IRollbackJournalStore`.
- Elevation IPC framing (HMAC handshake, PID plus start-time verification, process spawn, pipe serialization) stays internal behind `IElevatedCommandGateway`.
- UI pipe framing (binary `MessageSerializer`, twenty-five message types, `PipeConnectionOptions`) stays internal behind `IUiChannel`.
- Secure property DPAPI wrap, zeroing lifecycle, and wire format stay internal. Callers pass `SensitiveBytes` and never see the blob.

## Dependency Strategy

This is a **Ports and Adapters** design. Every cross-boundary dependency is an explicit port; every in-process helper deepens into the module as concrete code.

### Ports (external and remote-but-owned boundaries)

| Port | Category | Replaces |
|---|---|---|
| `IElevatedCommandGateway` | Remote-but-owned (cross-process elevation IPC) | `IElevationClient` + `PipeServer` + `ElevatedProcess` + HMAC handshake + `ProcessLauncher` |
| `IUiChannel` | Remote-but-owned (cross-process UI IPC) | `PipeServer` usage inside handlers |
| `IPayloadSource` | Local-substitutable (network I/O) | `PayloadDownloader` + `UpdateDownloader` + `DeltaApplicator` HTTP path |
| `IRollbackJournalStore` | Local-substitutable (durable storage) | `RollbackJournal` persistence layer |
| `IPayloadCache` | Local-substitutable (disk cache) | `PackageCache` + `CacheLayout` path defense |
| `ILayoutStore` | Local-substitutable (manifest persistence) | `LayoutManager` |
| `IFileSystem` | Local-substitutable (existing) | kept as-is |
| `IMsiApi` | Local-substitutable on Windows (existing) | kept as-is |
| `IProcessRunner` | Local-substitutable (existing) | kept as-is; elevation's process spawn moves to `IElevatedCommandGateway` |
| `IRestartManager` | True external, Windows native (existing) | kept as-is |
| `IEngineLogger` | Local-substitutable (existing) | kept as-is |
| `ISystemClock` | In-process time source | all `DateTime.UtcNow` calls |
| `IRandomSource` | In-process entropy source | `Guid.NewGuid()` and HMAC nonce generation |

`ISystemClock` and `IRandomSource` are side-benefit ports that unblock the current assessment Category 25 (Test Isolation and Determinism) scoring gap. They are cheap — roughly twenty lines of adapter code each — and permanently remove the `DateTime.UtcNow` and `Guid.NewGuid()` friction points throughout the engine.

### What stays in-process (no port)

- `VariableStore`, `BuiltInVariables`, `SecureVariable`, condition evaluator, condition lexer, built-in variable blocklist. All pure in-memory state. Tests use real instances.
- `Planner`, `MsiDetector`, `DependencyDetector`, `PackageDetector`. Pure logic over already-ported dependencies (`IFileSystem`, `IMsiApi`). Tests exercise them via real instances with fake adapters below.
- `RollbackExecutor`. Reads from `IRollbackJournalStore`, invokes undo operations through existing ports. No new port needed.
- `PackageExecutor`, `MsiExecutor`, `ExeExecutor`, `MsuExecutor`, `MspExecutor`, `BundleExecutor`. These *are* the pipeline domain logic and deepen into the module.
- All `IUndoOperation` implementations. Pure commands over existing ports.

### Production adapters

| Port | Production adapter | Wraps |
|---|---|---|
| `IElevatedCommandGateway` | `NamedPipeElevationGateway` | `ProcessLauncher`, `PipeServer` with HMAC handshake, typed message serializer, PID plus start-time verification |
| `IUiChannel` | `NamedPipeUiChannel` | UI-side `PipeServer`, typed request pump over `UiRequest` discriminated union |
| `IPayloadSource` | `HttpPayloadSource` | `HttpClient`, retry policy, SHA-256 verify, HTTPS-only guard, `DeltaApplicator` integration |
| `IRollbackJournalStore` | `FileSystemJournalStore` | `IFileSystem` append-only writes with `Flush(true)` for crash durability |
| `IPayloadCache` | `DiskPayloadCache` | `IFileSystem`, three-layer path-traversal defense, SHA-256 verification |
| `ILayoutStore` | `FileSystemLayoutStore` | `IFileSystem`, `LayoutJsonContext` AOT serializer |
| `ISystemClock` | `SystemClock` | `DateTime.UtcNow` |
| `IRandomSource` | `CryptoRandomSource` | `RandomNumberGenerator.Fill`, `Guid.NewGuid()` |

### Test adapters

| Port | Test adapter | Notes |
|---|---|---|
| `IElevatedCommandGateway` | `InProcessElevationGateway` | **Critical**. Serializes `ElevatedCommand` to bytes, deserializes, dispatches through the *real* `ElevatedCommandExecutor` whitelist with a real `FakeMsiApi`, serializes the result back. The bytes round-trip catches NativeAOT serialization bugs that would otherwise only surface in production. Supports `FailAfter(n)` hooks to simulate elevated-side crashes mid-apply. |
| `IUiChannel` | `FakeUiChannel` | `IAsyncEnumerable<UiRequest>` backed by a channel. Outbound events accumulate in a `List<PipelineEvent>` for assertions. Helper method `AwaitPhaseAsync(EnginePhase)` for state-machine tests. |
| `IPayloadSource` | `InMemoryPayloadSource` | `Dictionary<url, (bytes, sha256)>`. `ThrowOn(url)` simulates network failure. |
| `IRollbackJournalStore` | `InMemoryJournalStore` | `List<JournalEntry>` with `SimulateCrashAfter(n)` — proves crash-durability contract by making subsequent appends throw but preserving prior entries for `LoadAsync`. |
| `IPayloadCache` | `DictPayloadCache` | `ConcurrentDictionary<(packageId, sha256), byte[]>`. |
| `ILayoutStore` | `InMemoryLayoutStore` | Stores last-written manifest. |
| `IFileSystem` | existing `InMemoryFileSystem` from `FalkForge.Testing` | kept as-is |
| `IMsiApi` | existing `FakeMsiApi` | script installed product codes + configurable install outcomes |
| `IProcessRunner` | existing `FakeProcessRunner` | canned exit codes per exe path |
| `IRestartManager` | `NullRestartManager` | returns "no processes using files" |
| `IEngineLogger` | `ListLogger` | accumulates entries for assertion |
| `ISystemClock` | `FakeClock(DateTimeOffset start)` | frozen or advanceable |
| `IRandomSource` | `DeterministicRandom(seed)` | seeded PRNG, stable GUIDs |

Only `InProcessElevationGateway` is non-trivial. The key insight: the same `ElevatedCommandExecutor` class serves production (behind the pipe) and test (called directly). The port fakes the transport, not the policy — whitelist enforcement, security logging, and command dispatch are all exercised for real in tests.

## Testing Strategy

**Replace, don't layer.** Existing shallow unit tests on individual phase handlers get deleted. New boundary tests exercise `IInstallerPipeline` end-to-end with in-memory adapters, asserting observable outcomes through the public interface and through event-stream inspection.

### New boundary tests to write

At the pipeline level, using `InstallerPipelineBuilder` with full in-memory adapters:

1. **Happy-path install** — single MSI, absent → install → completed, zero rollback entries, correct event sequence.
2. **Happy-path upgrade** — older version detected, plan produces `MajorUpgradeAction`, apply succeeds, variable store reflects upgrade.
3. **Happy-path uninstall** — present → uninstall → completed, journal reflects uninstall entries.
4. **Plan transition legality** — `PlanAsync` called without prior `DetectAsync` returns `Result.Failure(ErrorKind.InvalidOperation)`; `ApplyAsync` without prior `PlanAsync` same.
5. **Force redetect** — second `DetectAsync` call after a successful plan invalidates the cached plan and requires re-planning.
6. **Mid-apply failure → rollback** — two-package bundle, second package scripted to fail, rollback runs journal entries in reverse, `ApplyReport` indicates failure, journal records both install and rollback entries, elevation gateway received the uninstall commands in correct order.
7. **Crash recovery** — journal has in-flight entries on pipeline construction, rollback resumes synchronously during `Build()`, first emitted event is `PhaseChanged(RollingBack)`, rollback completes cleanly, subsequent `DetectAsync` works normally.
8. **User cancel during apply** — `CancelAsync` triggered mid-install, rollback runs to completion, `ApplyReport` reflects cancelled-with-rollback status.
9. **Secure property flow** — `PlanRequest.SecureProperties` contains a password, the MSI executor receives it as an `MSIPROPERTY` value, logs contain no occurrence of the property value, zeroing verified on dispose.
10. **Elevated process crash simulation** — `InProcessElevationGateway.FailAfter(1)` triggers mid-apply, rollback attempts run but report elevation unavailability in `RollbackSummary`, terminal state is `RollbackFailed`, caller receives actionable error.
11. **Determinism** — two runs with identical inputs, `FakeClock` and `DeterministicRandom` produce byte-identical event streams, log sessions, and journal contents.

At the facade level, using `EngineSession.BindToPipe` with an in-memory `IUiChannel` substitute (injected through the session options or a test-only `BindToChannel` entry):

12. **UI pump** — `FakeUiChannel` pushes `Detect`, `Plan`, `Apply` requests in sequence, `RunUntilShutdown` completes, outbound channel messages match expected pipeline events, `EngineOutcome` reflects successful install.

### Old tests to delete

- All phase-handler-specific test classes that mock the full context bag. These are in `tests/FalkForge.Engine.Tests/Phases/` (filenames to be enumerated during implementation).
- Any test asserting against `EngineContext` mutations directly. Replace with assertions on `IInstallerPipeline` public outputs or `PipelineEvent` stream.
- Integration tests that construct `EngineStateMachine` directly to exercise transition logic. Replace with pipeline-level tests asserting transition legality via return values.

Tests that survive unchanged: `ExecutionTestFactory`-backed integration tests covering real-MSI compile-install-uninstall cycles. These already run against real `IMsiApi` on Windows and exercise the full pipeline.

### Test environment needs

- `FalkForge.Testing` gains new in-memory adapters for each new port (`InMemoryPayloadSource`, `InMemoryJournalStore`, `DictPayloadCache`, `InMemoryLayoutStore`, `FakeUiChannel`, `FakeClock`, `DeterministicRandom`, `InProcessElevationGateway`, `ListLogger`, `NullRestartManager`).
- `InProcessElevationGateway` requires a direct reference to `FalkForge.Engine.Elevation` from the testing assembly so tests can construct a real `ElevatedCommandExecutor`. Add `InternalsVisibleTo` from `FalkForge.Engine.Elevation` to `FalkForge.Testing` or expose a public test adapter constructor.
- No new global tools or external dependencies.

## Implementation Recommendations

Durable architectural guidance, decoupled from current file paths:

### What the module should own

- The install lifecycle state machine — every transition, every legality check, every automatic error route.
- The phase step implementations — detection, planning, elevation bring-up, applying, completing, rolling back, failing, shutting down. No caller ever touches a phase step type.
- The rollback journal replay logic and crash recovery — both at construction and during in-flight rollback.
- The variable store lifecycle including secure property zeroing.
- Progress fan-out and event stream composition.
- Cancellation token propagation from caller into phase steps and into port calls.
- UI-gated pause semantics: detecting when a phase step wants to wait for the next UI request and when it wants to continue automatically.

### What the module should hide

- Every one of the eleven current phase handler classes.
- The `EngineStateMachine` switch dispatcher.
- The `EngineContext` mutable bag and all implicit preconditions.
- The `EnginePhase` enum, except for the read-only `CurrentPhase` accessor and `PipelineEvent.PhaseChanged` event payload.
- The rollback journal on-disk format, entry types, and undo operation dispatcher.
- The HMAC handshake, PID plus start-time verification, process-spawn sequencing, and pipe framing used by the elevation gateway.
- The binary message framing, twenty-five message types, and `MessageSerializer` used by the UI channel.
- The DPAPI wrap and zeroing lifecycle of secure variables.
- The HTTP retry policy, SHA-256 verification, and delta-vs-full fallback inside `IPayloadSource`.
- The cache path-traversal defense layers inside `IPayloadCache`.

### What the module should expose

Two public surfaces:

1. **Production facade** — `EngineSession.BindToPipe(pipeName, manifestPath, options?)` returning an `IAsyncDisposable` session. One method to run it: `RunUntilShutdown(ct)`. One result type: `EngineOutcome` with rollback info auto-populated. Four-line `EngineHost.Main()`.
2. **Test and headless contract** — `IInstallerPipeline` with `StartAsync`, `DetectAsync`, `PlanAsync`, `ApplyAsync`, `CancelAsync`, plus `CurrentPhase` and `Events`. Constructed via `InstallerPipelineBuilder` with explicit per-port overrides. Five percent of callers touch this; ninety-five percent use the facade.

### How callers should migrate

**EngineHost callers** (production install path): no code changes required. The existing `Program.Main` body is rewritten to use `EngineSession.BindToPipe`, picking up all port wiring through the defaults helper.

**Test callers** that currently construct handlers directly: rewrite as `InstallerPipelineBuilder` instantiations with in-memory adapters. Assertion targets move from context-bag mutation checks to return values and event stream inspection.

**Future headless CLI callers** (`forge install foo.msi`): use `InstallerPipelineBuilder` directly, skip `EngineSession`, pump phases synchronously without a UI channel.

### Implementation sequencing

The deepening is a large refactor and should land through a TDD-driven plan with the commit sequence gates enforced per `CLAUDE.md`. Sketch of order:

1. **Define ports** — introduce the thirteen port interfaces with no production adapters yet. Move existing code to consume them through internal seams.
2. **Write production adapters** — wrap existing `PipeServer`, `PackageCache`, `RollbackJournal`, `PayloadDownloader`, `LayoutManager`, and elevation client code behind the new ports. One adapter per commit with a failing-first test.
3. **Write test adapters** — in-memory implementations added to `FalkForge.Testing`. One adapter per commit.
4. **Stand up `IInstallerPipeline`** — new internal pipeline type that replaces the state machine plus bag. Start empty, add phase steps one at a time behind failing tests. Keep old `EngineContext` and handlers alive in parallel during the transition.
5. **Migrate `EngineHost`** — rewrite `Main` to use the new pipeline through an interim adapter. Delete the old message loop.
6. **Write `EngineSession` facade** — wraps the pipeline with named-pipe wiring and the four-line `Main` story.
7. **Delete old handlers and `EngineContext`** — final pass. Phase handlers, state machine, and context bag removed once all tests pass against the new pipeline.

Each phase of the sequencing plan gets its own implementation plan file under `docs/plans/`, paired with this design document.
