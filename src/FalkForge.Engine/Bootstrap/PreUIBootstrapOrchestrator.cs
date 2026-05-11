namespace FalkForge.Engine.Bootstrap;

using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Coordinates the pre-UI prerequisite bootstrap sequence: detect missing packages,
/// install them (with elevation if required), and return the outcome so
/// <c>Program.RunAsBootstrapper</c> can decide whether to launch the UI or exit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Elevation model (three paths):</b>
/// <list type="number">
///   <item>
///     <description>
///       <b>No prereqs:</b> manifest has zero pre-UI packages → short-circuit, return
///       <see cref="PreUIBootstrapOutcome.LaunchUi"/> immediately (no detection work).
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Elevated child (<c>--bootstrap-elevated</c> flag set):</b> this IS the elevated
///       child spawned by an unelevated parent. Re-detect (defence-in-depth) and install.
///       Return <see cref="PreUIBootstrapOutcome.ExitSuccess"/> (or the appropriate failure
///       outcome) so the parent's <c>Environment.Exit</c> lets it continue to the UI launch.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Currently elevated, no flag:</b> user ran setup from an admin terminal.
///       Detect and install in-process (no UAC relaunch needed). Return
///       <see cref="PreUIBootstrapOutcome.LaunchUi"/> so the engine continues.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Unelevated, prereqs missing:</b> relaunch self elevated via
///       <see cref="IElevatedSelfRelauncher.Relaunch"/>. Map child exit code to outcome:
///       0 → <see cref="PreUIBootstrapOutcome.LaunchUi"/>;
///       2 → <see cref="PreUIBootstrapOutcome.ExitCancelled"/>;
///       other → <see cref="PreUIBootstrapOutcome.ExitFailed"/>.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>NativeAOT-safe:</b> no reflection, no dynamic code. Manual dependency injection.
/// </para>
/// </remarks>
public sealed class PreUIBootstrapOrchestrator
{
    // Exit-code sentinel: elevated child exited with "UAC dismissed" (cancellation).
    private const int ChildExitCancelled = 2;

    private readonly IPreUIPrerequisiteDetector _detector;
    private readonly IPreUIPrerequisiteInstaller _installer;
    private readonly IElevationProbe _elevationProbe;
    private readonly IElevatedSelfRelauncher _relauncher;
    private readonly IProgressSinkFactory _progressFactory;
    private readonly IEngineLogger? _logger;

    private const string Category = nameof(PreUIBootstrapOrchestrator);

    /// <summary>
    /// Creates a new <see cref="PreUIBootstrapOrchestrator"/>.
    /// </summary>
    public PreUIBootstrapOrchestrator(
        IPreUIPrerequisiteDetector detector,
        IPreUIPrerequisiteInstaller installer,
        IElevationProbe elevationProbe,
        IElevatedSelfRelauncher relauncher,
        IProgressSinkFactory progressFactory,
        IEngineLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(elevationProbe);
        ArgumentNullException.ThrowIfNull(relauncher);
        ArgumentNullException.ThrowIfNull(progressFactory);
        _detector        = detector;
        _installer       = installer;
        _elevationProbe  = elevationProbe;
        _relauncher      = relauncher;
        _progressFactory = progressFactory;
        _logger          = logger;
    }

    /// <summary>
    /// Executes the pre-UI bootstrap sequence and returns the outcome the caller should act on.
    /// </summary>
    /// <param name="manifest">Installer manifest containing the pre-UI package declarations.</param>
    /// <param name="args">Parsed bootstrapper flags (elevation, cache dir).</param>
    /// <param name="extractionDir">Absolute path to the extraction cache directory.</param>
    /// <param name="ownExecutablePath">Absolute path to this engine executable.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<PreUIBootstrapOutcome> RunAsync(
        InstallerManifest manifest,
        BootstrapperArgs args,
        string extractionDir,
        string ownExecutablePath,
        CancellationToken ct)
    {
        // Path 1 — no pre-UI packages declared: short-circuit, no detection or install work.
        if (manifest.PreUIPackages.Length == 0)
            return PreUIBootstrapOutcome.LaunchUi;

        // Path 2 — elevated child (--bootstrap-elevated flag set):
        //   Re-detect (defence-in-depth against tampering between extraction and elevation)
        //   then install. Return ExitSuccess/failure so the unelevated parent can continue.
        if (args.IsBootstrapElevated)
        {
            _logger?.Info(Category, "Running as elevated bootstrap child — detecting and installing.");
            var missing = _detector.FindMissing(manifest.PreUIPackages);

            if (missing.Count == 0)
            {
                _logger?.Info(Category, "All prerequisites satisfied after re-detection — no install needed.");
                return PreUIBootstrapOutcome.ExitSuccess;
            }

            return await InstallAndMapOutcomeAsync(missing, isElevatedChild: true, ct).ConfigureAwait(false);
        }

        // Paths 3 & 4 — detect first (common to both remaining branches).
        var missingPackages = _detector.FindMissing(manifest.PreUIPackages);

        if (missingPackages.Count == 0)
        {
            _logger?.Info(Category, "All prerequisites already satisfied.");
            return PreUIBootstrapOutcome.LaunchUi;
        }

        // Path 3 — already elevated (user ran from admin terminal), no flag:
        //   Install in-process; no relaunch needed. Return LaunchUi so the engine continues.
        if (_elevationProbe.IsElevated())
        {
            _logger?.Info(Category, $"{missingPackages.Count} prerequisite(s) missing — process already elevated, installing in-process.");
            return await InstallAndMapOutcomeAsync(missingPackages, isElevatedChild: false, ct).ConfigureAwait(false);
        }

        // Path 4 — unelevated with missing packages: relaunch elevated.
        // The parent supplies the extraction cache dir to the child via --cache-dir.
        // The child re-detects and installs, then exits. The parent maps the child's exit code.
        var cacheDir = string.IsNullOrEmpty(args.CacheDir) ? extractionDir : args.CacheDir;
        _logger?.Info(Category, $"{missingPackages.Count} prerequisite(s) missing — relaunching elevated.");

        int childExit = _relauncher.Relaunch(ownExecutablePath, cacheDir);
        return MapRelaunchExitCode(childExit);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a progress sink, runs the installer, and maps the result to an outcome.
    /// When <paramref name="isElevatedChild"/> is <see langword="true"/>, success maps to
    /// <see cref="PreUIBootstrapOutcome.ExitSuccess"/> (child exits to parent).
    /// When <see langword="false"/>, success maps to <see cref="PreUIBootstrapOutcome.LaunchUi"/>
    /// (in-process install completed; engine continues).
    /// </summary>
    private async Task<PreUIBootstrapOutcome> InstallAndMapOutcomeAsync(
        List<PreUIPackageInfo> missing,
        bool isElevatedChild,
        CancellationToken ct)
    {
        using var sink = _progressFactory.Create();
        var result = await _installer.RunAllAsync(missing, sink, ct).ConfigureAwait(false);

        return result switch
        {
            PreUIResult.Success    => isElevatedChild
                                         ? PreUIBootstrapOutcome.ExitSuccess
                                         : PreUIBootstrapOutcome.LaunchUi,
            PreUIResult.Cancelled  => PreUIBootstrapOutcome.ExitCancelled,
            PreUIResult.Failed     => PreUIBootstrapOutcome.ExitFailed,
            PreUIResult.RebootRequired => PreUIBootstrapOutcome.ExitRebootRequired,
            _                      => PreUIBootstrapOutcome.ExitFailed, // defensive: unknown variant
        };
    }

    /// <summary>Maps the elevated child's process exit code to a bootstrap outcome.</summary>
    private static PreUIBootstrapOutcome MapRelaunchExitCode(int exitCode) => exitCode switch
    {
        0                  => PreUIBootstrapOutcome.LaunchUi,        // child succeeded → parent continues to UI
        ChildExitCancelled => PreUIBootstrapOutcome.ExitCancelled,   // UAC dismissed or user cancelled
        _                  => PreUIBootstrapOutcome.ExitFailed,      // any other non-zero = failure
    };
}
