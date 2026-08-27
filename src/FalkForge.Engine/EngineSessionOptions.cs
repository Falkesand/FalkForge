namespace FalkForge.Engine;

using FalkForge.Diagnostics;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Transport;

/// <summary>
/// Optional configuration for <see cref="EngineSession.BindToPipe"/>.
/// All properties are optional; sensible defaults are used when omitted.
/// </summary>
public sealed record EngineSessionOptions
{
    /// <summary>
    /// Logger to use for the session. When <c>null</c> a default file-based
    /// <see cref="EngineLogger"/> is created at <see cref="EngineLogger.GetDefaultLogPath"/>.
    /// </summary>
    public IFalkLogger? Logger { get; init; }

    /// <summary>
    /// Directory to write log files to. Overrides the default temp-path strategy
    /// when <see cref="Logger"/> is also <c>null</c>.
    /// </summary>
    public string? LogDirectory { get; init; }

    /// <summary>
    /// Explicit log file path. When non-null and <see cref="Logger"/> is null, the
    /// session creates an <see cref="EngineLogger"/> at this path instead of using
    /// the default temp-path strategy or <see cref="LogDirectory"/>. Honoured by
    /// both <see cref="EngineSession.BindToPipe"/> and <see cref="EngineSession.BindToChannel"/>.
    /// </summary>
    public string? LogPath { get; init; }

    /// <summary>
    /// Minimum log level for the session-owned logger. When non-null and <see cref="Logger"/>
    /// is null, the freshly constructed logger has its <see cref="IFalkLogger.MinimumLevel"/>
    /// set to this value before any log call. When <see cref="Logger"/> is supplied, this
    /// value is applied to it as well so that the runtime override on the command-line
    /// overrides any default the host pre-configured.
    /// </summary>
    public LogLevel? MinimumLogLevel { get; init; }

    /// <summary>
    /// Named-pipe connection options (timeout, message size, security callback).
    /// Applied on top of the resolved <paramref name="pipeName"/> / <paramref name="sharedSecret"/>
    /// arguments in <see cref="EngineSession.BindToPipe"/>.
    /// </summary>
    public PipeConnectionOptions? PipeOptions { get; init; }

    /// <summary>
    /// Timeout for the initial HMAC handshake with the UI process.
    /// Defaults to 60 seconds when <c>null</c>.
    /// </summary>
    public TimeSpan? HandshakeTimeout { get; init; }

    /// <summary>
    /// The UI process the caller started, so the handshake wait can end the moment it dies and can
    /// say which way it failed. When <c>null</c> (any caller that did not start a UI itself) the
    /// wait behaves as before: it runs until <see cref="HandshakeTimeout"/> and reports the
    /// transport's own error.
    /// <para>
    /// Supplying it changes three things. A UI that exits ends the wait immediately instead of
    /// running the timeout down. The failure message names the exit code when the process died, or
    /// the process id when it is still running and silent. And the process is terminated on
    /// handshake failure, so a UI stuck on a modal dialog does not outlive the engine.
    /// </para>
    /// </summary>
    public IUiProcessHandle? UiProcess { get; init; }

    /// <summary>
    /// When <c>true</c> (default), a <see cref="FileSystemJournalStore"/> is created
    /// and wired into the pipeline to support rollback.
    /// </summary>
    public bool WriteJournal { get; init; } = true;

    /// <summary>
    /// Optional clock used to stamp the log filename when <see cref="LogDirectory"/> is
    /// provided (the branch that builds <c>install_{timestamp}.log</c> inline).
    /// Also forwarded to <see cref="EngineLogger.GetDefaultLogPath"/> when neither
    /// <see cref="LogPath"/> nor <see cref="LogDirectory"/> is set.
    /// When <c>null</c>, real wall-clock time is used (production default).
    /// Primarily for test isolation — inject a <see cref="FalkForge.Testing.FakeClock"/>
    /// to make log-path assertions deterministic.
    /// </summary>
    public ISystemClock? Clock { get; init; }

    /// <summary>
    /// Answers "is this engine process itself running elevated". Optional; defaults to the real
    /// Windows token probe.
    /// <para>
    /// The engine's own manifest is <c>asInvoker</c>, so the normal double-click flow runs
    /// unelevated and reaches per-machine work through the elevated companion. A user who picks
    /// "Run as administrator" instead gets an elevated engine, which can install per-machine
    /// directly. That run must keep doing so, because requiring the companion's baked publisher
    /// key there would refuse an install that works today on every build that carries no baked
    /// key, which is every published build.
    /// </para>
    /// <para>
    /// Two mechanisms answer questions like this in the codebase and they are not the same
    /// question. <c>WindowsEnvironment.IsElevated</c> asks about group membership through
    /// <c>WindowsPrincipal.IsInRole</c>, and <c>BuiltInVariables.PopulateSessionInfo</c> consumes
    /// that one for the <c>Privileged</c> variable. This seam wraps
    /// <c>ElevationProbe.IsElevated</c>, which asks whether THIS process token is the elevated
    /// one via <c>GetTokenInformation(TokenElevation)</c>. That is the right question here,
    /// because what the caller needs to know is whether <c>MsiInstallProductW</c> will succeed in
    /// this process.
    /// </para>
    /// <para>
    /// Internal: the only production reader is <see cref="EngineSession.BindToPipe"/> in this same
    /// assembly. Nothing outside it needs to override the probe; tests reach this through
    /// <c>InternalsVisibleTo</c>.
    /// </para>
    /// </summary>
    internal IElevationProbe? ElevationProbe { get; init; }

    // ──────────────────────────────────────────────────────────────────────────
    // Plan-only mode options
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, the session runs only through detection and planning,
    /// then exports the plan and exits without invoking Apply. Intended for
    /// <c>forge plan</c> / headless CI use cases.
    /// </summary>
    public bool IsPlanOnly { get; init; }

    /// <summary>
    /// Optional path for plan JSON output when <see cref="IsPlanOnly"/> is <c>true</c>.
    /// When <c>null</c>, the plan JSON is written to stdout. Ignored when
    /// <see cref="IsPlanOnly"/> is <c>false</c>.
    /// </summary>
    public string? PlanOnlyOutputPath { get; init; }

    /// <summary>
    /// When <c>true</c> (set by the engine on the require-signed update path), the pipeline advances the
    /// anti-downgrade/revocation trust store after a successful apply (C16), forwarding the manifest
    /// signature's epoch + revocations to the elevated companion. A fresh install leaves this <c>false</c>,
    /// so the store is never advanced by a first-time install.
    /// </summary>
    public bool AdvanceTrustStoreOnVerifiedApply { get; init; }

    /// <summary>
    /// Escape hatch for the runtime dependency-enforcement gate (<c>--ignore-dependencies</c>): when
    /// <c>true</c>, bypasses both the uninstall-blocking-dependents check and the install-missing-provider
    /// check in <see cref="Pipeline.PlanStep"/>. Defaults to <c>false</c>. Not implied by silent mode —
    /// silent uninstall is automation, exactly where silent breakage hurts most.
    /// </summary>
    public bool IgnoreDependencies { get; init; }

    /// <summary>
    /// Full path to a VERIFIED elevation companion executable
    /// (<c>FalkForge.Engine.Elevation.exe</c>) the session should launch for elevated commands,
    /// taking precedence over the default probe beside the engine
    /// (<c>AppContext.BaseDirectory</c>). Set by the self-extract bootstrapper AFTER
    /// <c>BootstrapCompanionResolver</c> has proven the extracted companion's bytes bind to the
    /// bundle manifest's declared (and, for signed bundles, ECDSA-covered) hash.
    /// <para>
    /// SECURITY: the companion runs elevated (SYSTEM for per-machine installs). Callers must only
    /// ever set this to a path whose contents have been integrity-verified — never to an
    /// unverified or attacker-influencable location. When null (or the file no longer exists),
    /// the session falls back to the beside-the-engine probe; when neither yields a companion the
    /// session runs without an elevation gateway (per-user behavior).
    /// </para>
    /// </summary>
    public string? ElevationCompanionPath { get; init; }

    /// <summary>
    /// The SHA-256 digest, as 64 hexadecimal characters, that
    /// <see cref="ElevationCompanionPath"/> was proven to have when it was verified.
    /// <para>
    /// SECURITY: this is required whenever <see cref="ElevationCompanionPath"/> is set. The
    /// session opens the file, hashes it, and compares it against this value immediately before
    /// wiring the elevation gateway, then keeps that handle open until the session ends. A path
    /// with no digest cannot be re-proven, so the session refuses to wire it and runs per-user
    /// instead. Verifying a file at extraction time and launching it by name minutes later is not
    /// a check at all: the extraction directory belongs to the user, and any process running as
    /// that user can swap the file, or redirect the path with a directory junction, in between.
    /// </para>
    /// </summary>
    public string? ElevationCompanionSha256 { get; init; }

    /// <summary>
    /// How the session resolves the elevation companion. The default
    /// (<see cref="ElevationCompanionPolicy.AmbientAllowed"/>) preserves the plain-engine-run
    /// behavior: probe beside the engine when no verified path is supplied. The self-extract
    /// bootstrapper ALWAYS overrides this with <see cref="ElevationCompanionPolicy.VerifiedPath"/>
    /// (manifest declares a companion, verified by <c>BootstrapCompanionResolver</c>) or
    /// <see cref="ElevationCompanionPolicy.NoneDeclared"/> (manifest declares none), because in a
    /// bundle bootstrap the manifest is authoritative and the ambient probe would let a planted
    /// <c>FalkForge.Engine.Elevation.exe</c> beside the bundle exe be launched elevated unverified.
    /// </summary>
    public ElevationCompanionPolicy ElevationCompanionPolicy { get; init; }

    /// <summary>
    /// The persisted anti-downgrade epoch loaded (and ACL-validated) by the bootstrapper on the
    /// require-signed update path. When non-null, the pipeline's apply-time integrity gate runs with the
    /// update-path trust policy: it resolves Update vs KeyChange from the signed epoch against this value
    /// (the same C19 quorum resolution the staged-update verifier applies) and enforces anti-downgrade,
    /// so the path that advances the persisted trust store cannot accept a key change under the weaker
    /// fresh-install rule. Null (the default) keeps the fresh-install policy.
    /// </summary>
    public int? UpdatePathStoredEpoch { get; init; }

    /// <summary>
    /// Root directory the self-extract bootstrapper unpacked the bundle's payloads into (each payload at
    /// <c>{PayloadRoot}/{PackageId}</c>). When set, the pipeline resolves every package's install path to
    /// its extracted location under this root instead of the manifest's build-machine
    /// <see cref="Protocol.Manifest.PackageInfo.SourcePath"/> — this is what lets a distributed bundle
    /// install on a machine other than the one it was built on. Null (the default) on the
    /// <c>--manifest</c> / <c>forge plan</c> / offline-layout path, where SourcePath is genuinely the
    /// path to use and behaviour must stay unchanged.
    /// </summary>
    public string? PayloadRoot { get; init; }

    // ──────────────────────────────────────────────────────────────────────────
    // Test-only injection point (exposed via EngineSession.BindToChannel)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test-only: pre-built UI channel. When non-null, <see cref="EngineSession.BindToPipe"/>
    /// skips named-pipe setup and uses this channel directly.
    /// Consumed by <see cref="EngineSession.BindToChannel"/>.
    /// </summary>
    internal IUiChannel? Channel { get; init; }

    /// <summary>
    /// Test-only: installer manifest to seed into the pipeline when using
    /// <see cref="EngineSession.BindToChannel"/>. Enables plan-only integration tests
    /// without requiring a named-pipe or a real manifest file on disk.
    /// Ignored by <see cref="EngineSession.BindToPipe"/>, which takes its manifest from
    /// <see cref="VerifiedManifest"/> when that is supplied and otherwise loads the file at
    /// <c>manifestPath</c>.
    /// </summary>
    internal FalkForge.Engine.Protocol.Manifest.InstallerManifest? SeedManifest { get; init; }

    // ──────────────────────────────────────────────────────────────────────────
    // Publisher-verified manifest (production; bundle bootstrap only)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The installer manifest the caller has already proved came from the publisher. When this is
    /// non-null, <see cref="EngineSession.BindToPipe"/> uses this object and never reads the file at
    /// <c>manifestPath</c>.
    ///
    /// <para><b>Why this exists.</b> The bundle bootstrapper deserializes the manifest from the
    /// bundle's own embedded bytes, runs <c>BundleTrustGate.Verify</c> on that object, and then writes
    /// the same JSON to <c>{cacheDir}\manifest.json</c> so the separate UI process can read it. That
    /// file sits under the unelevated user's <c>%TEMP%</c>, so same-user code at medium integrity can
    /// rewrite it. Re-reading it here would let an attacker choose the package list, the payload
    /// digests forwarded to the elevated companion, the update feed and its pinned publisher
    /// thumbprint, and the dependency records written to HKLM. Handing the verified object across
    /// instead removes the file as an input rather than adding another check over it.</para>
    ///
    /// <para>This is NOT <see cref="SeedManifest"/>, which is a test-only hook honoured only by
    /// <see cref="EngineSession.BindToChannel"/>. Only pass a manifest here that the caller verified
    /// itself.</para>
    /// </summary>
    internal FalkForge.Engine.Protocol.Manifest.InstallerManifest? VerifiedManifest { get; init; }
}
