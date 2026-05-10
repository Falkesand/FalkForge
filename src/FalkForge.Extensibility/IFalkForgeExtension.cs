namespace FalkForge.Extensibility;

public interface IFalkForgeExtension
{
    /// <summary>
    /// Unique identifier of the extension. Used for conflict detection during registration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Semantic version of this extension implementation (e.g. "1.0.0"). Defaults to
    /// <c>"0.0.0"</c> when an extension does not override it so existing extensions
    /// require no source changes. Surfaces in error messages and allows consumers to
    /// log the active extension set for diagnostics.
    /// </summary>
    string Version => "0.0.0";

    /// <summary>
    /// Minimum FalkForge host version this extension supports, as a dotted numeric
    /// version string (parsed via <see cref="System.Version"/>). Defaults to
    /// <c>"0.0.0"</c> meaning the extension is compatible with any host version.
    /// <see cref="ExtensionRegistration.Register"/> rejects the extension with a
    /// <see cref="PluginCompatibilityException"/> if the current host version is
    /// lower than this value, or if this value cannot be parsed as a
    /// <see cref="System.Version"/>.
    /// </summary>
    string MinHostVersion => "0.0.0";

    void Register(IExtensionRegistry registry);
}
