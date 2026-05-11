namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Factory that creates and owns an <see cref="IProgressSink"/> for a single installation run.
/// Allows tests to inject a non-UI sink while production code wires <c>TaskDialogProgress</c>.
/// </summary>
public interface IProgressSinkFactory
{
    /// <summary>
    /// Creates an <see cref="IProgressSink"/> ready to receive progress notifications.
    /// The caller is responsible for disposing the returned instance if it implements
    /// <see cref="IDisposable"/>.
    /// </summary>
    IProgressSink Create();
}
