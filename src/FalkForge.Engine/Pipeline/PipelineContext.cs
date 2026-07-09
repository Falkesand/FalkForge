namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.RestartManager;

/// <summary>
/// Mutable state bag threaded through all pipeline phase steps.
/// Populated progressively: Detect fills <see cref="Manifest"/> +
/// <see cref="Detection"/>; Plan fills <see cref="Plan"/>.
/// </summary>
internal sealed class PipelineContext
{
    // ──────────────────────────────────────────────────────────────────────────
    // Set at pipeline construction time
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The installer manifest provided at pipeline construction or loaded from
    /// the layout store. Never null after <see cref="DetectStep"/> completes.
    /// </summary>
    public InstallerManifest? Manifest { get; set; }

    // ──────────────────────────────────────────────────────────────────────────
    // Populated by DetectStep
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Current installation state of all packages.</summary>
    public DetectionResult? Detection { get; set; }

    /// <summary>
    /// Related bundles detected on the machine (upgrade candidates, etc.).
    /// Empty list when none found.
    /// </summary>
    public IReadOnlyList<RelatedBundleInfo> RelatedBundles { get; set; } = [];

    /// <summary>
    /// Available update discovered during detection, or null when no update feed
    /// is configured or the current version is up to date.
    /// </summary>
    public UpdateCheckResult? AvailableUpdate { get; set; }

    // ──────────────────────────────────────────────────────────────────────────
    // Populated by PlanStep
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Action requested by the UI (Install / Uninstall / Repair / Modify).</summary>
    public UiRequest.Plan? PlanRequest { get; set; }

    /// <summary>The installation plan produced by <see cref="PlanStep"/>.</summary>
    public InstallPlan? Plan { get; set; }

    /// <summary>
    /// When true, <see cref="PipelineRunner"/> exits after the Plan phase without
    /// invoking Apply. Set via <see cref="InstallerPipelineBuilder.WithPlanOnlyMode"/>.
    /// </summary>
    public bool IsPlanOnly { get; set; }

    /// <summary>
    /// Optional path for plan JSON output in plan-only mode.
    /// When null, the plan JSON is written to stdout.
    /// </summary>
    public string? PlanOnlyOutputPath { get; set; }

    /// <summary>
    /// When true the engine runs without UI interaction. License acceptance is
    /// automatic and no prompts are shown. Set via
    /// <see cref="InstallerPipelineBuilder.WithSilentMode"/>.
    /// </summary>
    public bool SilentMode { get; set; }

    // ──────────────────────────────────────────────────────────────────────────
    // Apply options — set at construction, consumed by ApplyStep
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When true, <see cref="ApplyStep"/> simulates execution instead of running
    /// real installers. All actions are logged; nothing is installed.
    /// </summary>
    public bool IsDryRun { get; set; }

    /// <summary>
    /// Path for the dry-run simulation log. Only consulted when <see cref="IsDryRun"/> is true.
    /// When null the log is written to stdout via <see cref="IUiChannel"/>.
    /// </summary>
    public string? DryRunLogPath { get; set; }

    /// <summary>
    /// Optional Restart Manager used by <see cref="ApplyStep"/> to gracefully
    /// shut down processes holding files in use before installation.
    /// Null means Restart Manager integration is disabled.
    /// </summary>
    public IRestartManager? RestartManager { get; set; }

    /// <summary>
    /// The trust policy consumed by the integrity gate in <see cref="ApplyStep"/>: the pinned
    /// publisher-key fingerprints (authorship) and whether a signature is required. Defaults to a
    /// fresh-install policy pinned to the engine's baked trusted set
    /// (<see cref="FalkForge.Engine.Integrity.BakedTrustedKeys"/>) — an attacker's re-signed bundle is
    /// rejected because its key is not pinned, while an engine built with no baked keys falls back to
    /// consistency-only verification. Overridable for tests.
    /// </summary>
    public FalkForge.Engine.Integrity.TrustPolicy IntegrityTrustPolicy { get; set; } =
        FalkForge.Engine.Integrity.TrustPolicy.FreshInstall(FalkForge.Engine.Integrity.BakedTrustedKeys.Fingerprints);

    // ──────────────────────────────────────────────────────────────────────────
    // Populated by ElevateStep
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The elevation gateway after a successful <c>ElevateStep</c>.
    /// Null when elevation was not required (PerUser) or not yet started.
    /// </summary>
    public IElevatedCommandGateway? ElevationGateway { get; set; }

    // ──────────────────────────────────────────────────────────────────────────
    // Populated by ApplyStep
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Whether any executed package requested a reboot.</summary>
    public bool RebootRequired { get; set; }
}
