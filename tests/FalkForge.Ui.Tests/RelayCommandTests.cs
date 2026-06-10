using System.Windows.Input;
using FalkForge.Ui;
using Xunit;

namespace FalkForge.Ui.Tests;

/// <summary>
/// Verifies that exceptions thrown by the async execute delegate in RelayCommand
/// are routed to the UnhandledException callback rather than being swallowed silently.
/// </summary>
public class RelayCommandTests
{
    [WpfFact]
    public async Task Execute_AsyncDelegateThrows_RoutesToUnhandledExceptionCallback()
    {
        // Arrange
        var expectedException = new InvalidOperationException("test error");
        Exception? captured = null;
        var tcs = new TaskCompletionSource();

        RelayCommand.UnhandledException = ex =>
        {
            captured = ex;
            tcs.TrySetResult();
        };

        try
        {
            ICommand cmd = new RelayCommand(() => Task.FromException(expectedException));

            // Act — async void fires and returns; we wait for callback
            cmd.Execute(null);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.NotNull(captured);
            Assert.Same(expectedException, captured);
        }
        finally
        {
            RelayCommand.UnhandledException = null;
        }
    }

    [WpfFact]
    public async Task Execute_AsyncDelegateThrowsOperationCanceled_DoesNotRouteToCallback()
    {
        // Arrange — OperationCanceledException must remain silently swallowed
        Exception? captured = null;
        var completionTcs = new TaskCompletionSource();

        RelayCommand.UnhandledException = ex =>
        {
            captured = ex;
            completionTcs.TrySetResult();
        };

        try
        {
            ICommand cmd = new RelayCommand(() => Task.FromCanceled(new CancellationToken(canceled: true)));

            // Act
            cmd.Execute(null);

            // Give it a moment — callback must NOT fire
            await Task.Delay(200);

            // Assert
            Assert.Null(captured);
        }
        finally
        {
            RelayCommand.UnhandledException = null;
        }
    }

    [WpfFact]
    public async Task Execute_AsyncDelegateThrows_NullCallback_DoesNotThrow()
    {
        // Arrange — when no callback set, exception must not propagate (no crash)
        RelayCommand.UnhandledException = null;

        var tcs = new TaskCompletionSource();
        ICommand cmd = new RelayCommand(async () =>
        {
            await Task.Yield();
            tcs.TrySetResult();
            throw new InvalidOperationException("should not crash");
        });

        // Act
        cmd.Execute(null);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100); // let the exception path complete

        // Assert — reaching here means no unhandled exception escaped.
        Assert.True(tcs.Task.IsCompleted, "Delegate ran to the throw point without crashing the caller.");
    }
}
