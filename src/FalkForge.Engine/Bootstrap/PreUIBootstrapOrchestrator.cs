namespace FalkForge.Engine.Bootstrap;

using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Coordinates the pre-UI prerequisite bootstrap sequence: detect missing packages, install
/// them when the process is already elevated, and return the result so
/// <c>BootstrapperRunner.RunAsync</c> can decide whether to launch the UI or exit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Elevation model (two paths):</b>
/// <list type="number">
///   <item>
///     <description>
///       <b>No prereqs, or none missing:</b> the manifest declares zero pre-UI packages, or
///       every declared package is already installed → return
///       <see cref="PreUIBootstrapOutcome.LaunchUi"/> without doing any install work.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Missing prerequisites, process already elevated</b> (the user ran setup from an
///       admin terminal): detect and install in-process, no relaunch of any kind. Return
///       <see cref="PreUIBootstrapOutcome.LaunchUi"/> on success so the engine continues.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Missing prerequisites, process NOT elevated:</b> report failure instead of asking for
/// administrator rights. Returns <see cref="PreUIBootstrapOutcome.ExitFailed"/> together with
/// the missing prerequisites' display names
/// (<see cref="PreUIBootstrapResult.MissingPrerequisiteNames"/>) so the caller can tell the user
/// what to install manually before running setup again.
/// </para>
/// <para>
/// A previous version of this class relaunched itself elevated (via the Windows shell's
/// <c>runas</c> verb) when prerequisites were missing and the process was not already elevated,
/// then had the elevated child install them. Both the relaunch and the elevated-child path it
/// fed have been removed: the shell resolves the <c>runas</c> verb through a per-user registry
/// key that any process running as the same user can rewrite, so the relaunch target was not
/// trustworthy, and measurement showed the relaunched child could never actually complete a
/// prerequisite install in practice. Redesigning the relaunch to be trustworthy was not worth it
/// when the path it protected did not work.
/// </para>
/// <para>
/// <b>NativeAOT-safe:</b> no reflection, no dynamic code. Manual dependency injection.
/// </para>
/// </remarks>
public sealed class PreUIBootstrapOrchestrator
{
    private readonly IPreUIPrerequisiteDetector _detector;
    private readonly IPreUIPrerequisiteInstaller _installer;
    private readonly IElevationProbe _elevationProbe;
    private readonly IProgressSinkFactory _progressFactory;
    private readonly IFalkLogger? _logger;

    private const string Category = nameof(PreUIBootstrapOrchestrator);

    /// <summary>
    /// Creates a new <see cref="PreUIBootstrapOrchestrator"/>.
    /// </summary>
    public PreUIBootstrapOrchestrator(
        IPreUIPrerequisiteDetector detector,
        IPreUIPrerequisiteInstaller installer,
        IElevationProbe elevationProbe,
        IProgressSinkFactory progressFactory,
        IFalkLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(elevationProbe);
        ArgumentNullException.ThrowIfNull(progressFactory);
        _detector        = detector;
        _installer       = installer;
        _elevationProbe  = elevationProbe;
        _progressFactory = progressFactory;
        _logger          = logger;
    }

    /// <summary>
    /// Executes the pre-UI bootstrap sequence and returns the result the caller should act on.
    /// </summary>
    /// <param name="manifest">Installer manifest containing the pre-UI package declarations.</param>
    /// <param name="extractionDir">Absolute path to the extraction cache directory.</param>
    /// <param name="ownExecutablePath">Absolute path to this engine executable.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<PreUIBootstrapResult> RunAsync(
        InstallerManifest manifest,
        string extractionDir,
        string ownExecutablePath,
        CancellationToken ct)
    {
        // No pre-UI packages declared: short-circuit, no detection or install work.
        if (manifest.PreUIPackages.Length == 0)
            return PreUIBootstrapResult.From(PreUIBootstrapOutcome.LaunchUi);

        var missingPackages = _detector.FindMissing(manifest.PreUIPackages);

        if (missingPackages.Count == 0)
        {
            _logger?.Info(Category, "All prerequisites already satisfied.");
            return PreUIBootstrapResult.From(PreUIBootstrapOutcome.LaunchUi);
        }

        // Already elevated (user ran from admin terminal): install in-process, no relaunch.
        if (_elevationProbe.IsElevated())
        {
            _logger?.Info(Category, $"{missingPackages.Count} prerequisite(s) missing — process already elevated, installing in-process.");
            var outcome = await InstallAndMapOutcomeAsync(missingPackages, ct).ConfigureAwait(false);
            return PreUIBootstrapResult.From(outcome);
        }

        // Unelevated with missing prerequisites: report failure instead of asking for
        // administrator rights. See the class remarks for why this no longer relaunches.
        var missingNames = missingPackages.ConvertAll(p => p.DisplayName);
        _logger?.Error(Category,
            $"{missingPackages.Count} prerequisite(s) missing and this process is not elevated. " +
            "Install them manually, then run setup again.");
        return new PreUIBootstrapResult(PreUIBootstrapOutcome.ExitFailed, missingNames);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a progress sink, runs the installer, and maps the result to an outcome.
    /// </summary>
    private async Task<PreUIBootstrapOutcome> InstallAndMapOutcomeAsync(
        List<PreUIPackageInfo> missing,
        CancellationToken ct)
    {
        using var sink = _progressFactory.Create();
        var result = await _installer.RunAllAsync(missing, sink, ct).ConfigureAwait(false);

        return result switch
        {
            PreUIResult.Success        => PreUIBootstrapOutcome.LaunchUi,
            PreUIResult.Cancelled      => PreUIBootstrapOutcome.ExitCancelled,
            PreUIResult.Failed         => PreUIBootstrapOutcome.ExitFailed,
            PreUIResult.RebootRequired => PreUIBootstrapOutcome.ExitRebootRequired,
            _                          => PreUIBootstrapOutcome.ExitFailed, // defensive: unknown variant
        };
    }
}
