namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Protocol;

/// <summary>
/// Discriminated union of all requests the UI can send to the installer pipeline.
/// Replaces the twenty-five raw <c>EngineMessage</c> subtypes at the pipeline boundary;
/// the <see cref="IUiChannel"/> implementation translates wire messages into these.
/// </summary>
public abstract record UiRequest
{
    private UiRequest() { }

    /// <summary>UI asks the engine to run package detection.</summary>
    public sealed record Detect : UiRequest;

    /// <summary>UI asks the engine to plan with the given action and user inputs.</summary>
    /// <param name="InstallDirectory">
    /// Where the UI asked for the product to go. <b>Nothing reads this yet.</b>
    /// <see cref="PlanStep"/> builds the plan from Action, LicenseAccepted, Properties,
    /// FeatureSelections, PackageFeatureSelections and SecureProperties only, so every package
    /// installs where its own MSI puts it. Deciding which package in a chain the directory applies
    /// to, and which property each one reads it from, is an open design question — see
    /// <c>PlanIgnoresInstallDirectoryTests</c>. The built-in wizard keeps its directory page out of
    /// the walk for exactly this reason rather than reporting success for a control that did
    /// nothing.
    /// </param>
    /// <param name="PackageFeatureSelections">
    /// Per-package interactive MSI feature selections: packageId → selected feature ids.
    /// Distinct from <paramref name="FeatureSelections"/> (whole-package, bundle-level
    /// feature gating); this drives the <c>ADDLOCAL</c> property for a single MSI package.
    /// Null when the UI advertised no per-package feature picker.
    /// </param>
    public sealed record Plan(
        InstallAction Action,
        string? InstallDirectory,
        IReadOnlyDictionary<string, bool> FeatureSelections,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyDictionary<string, SensitiveBytes> SecureProperties,
        bool? LicenseAccepted = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? PackageFeatureSelections = null) : UiRequest;

    /// <summary>UI confirms it is ready to start applying the plan.</summary>
    public sealed record Apply : UiRequest;

    /// <summary>UI requests cancellation of the in-progress operation.</summary>
    public sealed record Cancel : UiRequest;

    /// <summary>UI requests engine shutdown after the current phase completes.</summary>
    public sealed record Shutdown : UiRequest;

    /// <summary>UI requests that the downloaded update installer be launched.</summary>
    public sealed record LaunchUpdate : UiRequest;
}
