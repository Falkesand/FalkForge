namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Factory that creates and owns an <see cref="IProgressSink"/> for a single installation run.
/// Allows tests to inject a non-UI sink while production code wires <c>TaskDialogProgress</c>.
/// </summary>
public interface IProgressSinkFactory
{
    /// <summary>
    /// Creates an <see cref="IProgressSink"/> ready to receive progress notifications.
    /// The caller owns the returned instance and must dispose it when the run completes.
    /// </summary>
    IProgressSinkHandle Create();
}

/// <summary>
/// Combines <see cref="IProgressSink"/> with <see cref="IDisposable"/> so callers always
/// dispose the handle, ensuring native resources (e.g. TaskDialog window) are released.
/// </summary>
public interface IProgressSinkHandle : IProgressSink, IDisposable { }
