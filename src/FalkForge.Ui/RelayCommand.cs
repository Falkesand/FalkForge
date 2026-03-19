using System.Diagnostics;
using System.Windows.Input;

namespace FalkForge.Ui;

internal sealed class RelayCommand : ICommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Func<Task> _execute;

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
            // Prevent unhandled exceptions from crashing the application.
            // Engine errors are surfaced via StatusMessage on the ViewModel.
            Trace.TraceError($"RelayCommand: {ex}");
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}