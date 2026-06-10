using System.Diagnostics;
using System.Windows.Input;

namespace FalkForge.Ui;

internal sealed class RelayCommand : ICommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Func<Task> _execute;

    /// <summary>
    /// Optional callback invoked when the async execute delegate throws an unhandled exception
    /// (excluding <see cref="OperationCanceledException"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Static and process-wide.</strong> This property is shared by every
    /// <see cref="RelayCommand"/> instance in the process. Assigning it affects all commands,
    /// not only the one currently being configured.
    /// </para>
    /// <para>
    /// <strong>Single-subscriber, last-writer-wins.</strong> The setter replaces any previously
    /// registered handler. Unlike an event, there is no multicast accumulation — only the most
    /// recently assigned delegate is invoked.
    /// </para>
    /// <para>
    /// <strong>Setter is not thread-safe.</strong> Assign during application startup or test
    /// setup, before commands are invoked concurrently.
    /// </para>
    /// <para>
    /// <strong>Null fallback.</strong> When the value is <see langword="null"/>, exceptions fall
    /// back to <see cref="Trace.TraceError"/> so they are at least visible in a debug trace
    /// listener.
    /// </para>
    /// <para>
    /// <strong><see cref="OperationCanceledException"/> is never routed.</strong> Cancellation is
    /// treated as expected shutdown behaviour and is swallowed silently regardless of the handler.
    /// </para>
    /// <para>
    /// <strong>Test hygiene.</strong> Test code that sets this property must restore the previous
    /// value in a <see langword="try"/>/<see langword="finally"/> block to avoid leaking the
    /// override into subsequent tests.
    /// </para>
    /// </remarks>
    public static Action<Exception>? UnhandledException { get; set; }

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute();
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected during shutdown
        }
        catch (Exception ex)
        {
            // Route to the registered observer so callers can surface errors visibly
            // (e.g., set StatusMessage on a ViewModel). Fall back to Trace when none set.
            var handler = UnhandledException;
            if (handler is not null)
                handler(ex);
            else
                Trace.TraceError($"RelayCommand: {ex}");
        }
    }

    /// <summary>
    /// Raised when <see cref="CanExecute"/> may have changed.
    /// Delegates to <see cref="CommandManager.RequerySuggested"/>, which is a
    /// process-wide multicast event — every subscriber registered on any
    /// <see cref="RelayCommand"/> instance receives the notification, not just
    /// those targeting this command. This matches standard WPF ICommand
    /// semantics and is intentional: WPF's CommandManager batches
    /// re-evaluation of all commands in a single dispatcher pass.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}