namespace FalkForge.Extensibility;

/// <summary>
/// Thrown when an extension cannot be registered because its identity conflicts with
/// an already-registered extension or its <see cref="IFalkForgeExtension.MinHostVersion"/>
/// requirement is not satisfied by the current host.
/// </summary>
public sealed class PluginCompatibilityException : Exception
{
    public PluginCompatibilityException()
    {
    }

    public PluginCompatibilityException(string message)
        : base(message)
    {
    }

    public PluginCompatibilityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
