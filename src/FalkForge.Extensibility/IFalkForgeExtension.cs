namespace FalkForge.Extensibility;

public interface IFalkForgeExtension
{
    /// <summary>
    /// Unique identifier of the extension. Used for conflict detection during registration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Semantic version of this extension implementation (e.g. "1.0.0"). Defaults to
    /// "1.0.0" when an extension does not override it. Surfaces in error messages and
    /// allows consumers to log the active extension set for diagnostics.
    /// </summary>
    string Version => "1.0.0";

    /// <summary>
    /// Minimum host (Extensibility contract) semantic version required by this extension.
    /// When non-null, <see cref="ExtensionRegistration.Register"/> rejects the plugin
    /// if the host version is older. Defaults to <c>null</c> (no requirement).
    /// </summary>
    string? MinHostVersion => null;

    void Register(IExtensionRegistry registry);
}
