namespace FalkForge.Engine.Elevation;

using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Protocol.Messages;

public sealed class ElevatedCommandExecutor
{
    private readonly Dictionary<string, IElevatedCommand> _commands;

    public ElevatedCommandExecutor(IEnumerable<IElevatedCommand> commands)
    {
        _commands = commands.ToDictionary(c => c.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// The registered command names. Test-support accessor (InternalsVisibleTo) used to pin the
    /// SYSTEM-executable command surface — see ElevatedHostCommandSurfaceTests.
    /// </summary>
    internal IReadOnlyCollection<string> CommandNames => _commands.Keys;

    public ElevateResultMessage Execute(ElevateExecuteMessage message, Action<int>? onProgress = null)
    {
        if (!_commands.TryGetValue(message.CommandName, out var command))
        {
            return new ElevateResultMessage
            {
                SequenceId = message.SequenceId,
                Success = false,
                ErrorMessage = $"Unknown command: {message.CommandName}"
            };
        }

        var result = command.Execute(message.CommandPayload, onProgress);

        return result.Match(
            payload => new ElevateResultMessage
            {
                SequenceId = message.SequenceId,
                Success = true,
                ResultPayload = payload.Length > 0 ? payload : null
            },
            error => new ElevateResultMessage
            {
                SequenceId = message.SequenceId,
                Success = false,
                ErrorMessage = error.Message
            }
        );
    }
}
