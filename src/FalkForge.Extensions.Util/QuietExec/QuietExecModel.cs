namespace FalkForge.Extensions.Util.QuietExec;

public sealed class QuietExecModel
{
    public required string Id { get; init; }
    public required string CommandLine { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? Condition { get; init; }
    public string? RollbackCommandLine { get; init; }
}
