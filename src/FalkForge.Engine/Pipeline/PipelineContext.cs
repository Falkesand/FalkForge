namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;

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

    // ──────────────────────────────────────────────────────────────────────────
    // Populated by PlanStep
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Action requested by the UI (Install / Uninstall / Repair / Modify).</summary>
    public UiRequest.Plan? PlanRequest { get; set; }

    /// <summary>The installation plan produced by <see cref="PlanStep"/>.</summary>
    public InstallPlan? Plan { get; set; }

    // ──────────────────────────────────────────────────────────────────────────
    // Populated by ApplyStep
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Whether any executed package requested a reboot.</summary>
    public bool RebootRequired { get; set; }
}
