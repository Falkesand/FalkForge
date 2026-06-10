using System.Diagnostics;
using System.Windows.Input;

namespace FalkForge.Ui;

internal sealed class RelayCommand : ICommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Func<Task> _execute;

    /// <summary>
    /// Optional callback invoked when the async execute delegate throws an unhandled exception
    /// (excluding <see cref="OperationCanceledException"/>). Defaults to <see langword="null"/>,
    /// which falls back to <see cref="Trace.TraceError"/>. Inject in tests or application startup
    /// to route exceptions to a visible error surface (e.g., a ViewModel's StatusMessage).
    /// </summary>
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

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}