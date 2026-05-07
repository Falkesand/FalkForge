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
    public sealed record Plan(
        InstallAction Action,
        string? InstallDirectory,
        IReadOnlyDictionary<string, bool> FeatureSelections,
        IReadOnlyDictionary<string, string> Properties,
        IReadOnlyDictionary<string, SensitiveBytes> SecureProperties) : UiRequest;

    /// <summary>UI confirms it is ready to start applying the plan.</summary>
    public sealed record Apply : UiRequest;

    /// <summary>UI requests cancellation of the in-progress operation.</summary>
    public sealed record Cancel : UiRequest;

    /// <summary>UI requests engine shutdown after the current phase completes.</summary>
    public sealed record Shutdown : UiRequest;

    /// <summary>UI requests that the downloaded update installer be launched.</summary>
    public sealed record LaunchUpdate : UiRequest;
}
