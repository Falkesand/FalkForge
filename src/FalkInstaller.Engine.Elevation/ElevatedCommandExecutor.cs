namespace FalkInstaller.Engine.Elevation;

using FalkInstaller.Engine.Elevation.Commands;
using FalkInstaller.Engine.Protocol.Messages;

public sealed class ElevatedCommandExecutor
{
    private readonly Dictionary<string, IElevatedCommand> _commands;

    public ElevatedCommandExecutor(IEnumerable<IElevatedCommand> commands)
    {
        _commands = commands.ToDictionary(c => c.Name, StringComparer.Ordinal);
    }

    public ElevateResultMessage Execute(ElevateExecuteMessage message)
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

        var result = command.Execute(message.CommandPayload);

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
