using Spectre.Console.Cli;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Test-only bridge for invoking a command's protected <c>Execute</c> logic directly.
/// Spectre.Console.Cli 0.55 made <see cref="Command{TSettings}.Execute"/> protected (previously
/// public), reachable in production only via <see cref="ICommand{TSettings}.ExecuteAsync"/>
/// through <see cref="CommandApp"/>. These tests intentionally bypass <c>CommandApp</c> to invoke
/// command logic directly against a test console, so they call the interface method instead.
/// <see cref="ICommand{TSettings}.ExecuteAsync"/> wraps the synchronous result in
/// <c>Task.FromResult</c>, so the task is always already completed and blocking on it here is safe.
/// </summary>
internal static class CommandExecuteExtensions
{
    public static int ExecuteSync<TSettings>(
        this ICommand<TSettings> command,
        CommandContext context,
        TSettings settings,
        CancellationToken cancellationToken = default)
        where TSettings : CommandSettings =>
        command.ExecuteAsync(context, settings, cancellationToken).GetAwaiter().GetResult();
}
