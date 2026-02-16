namespace FalkForge.Compiler.Msi.Validation;

public sealed class IceMessage
{
    public required string IceName { get; init; }
    public required IceMessageSeverity Severity { get; init; }
    public required string Description { get; init; }
    public string? Table { get; init; }
    public string? Column { get; init; }
    public string? PrimaryKeys { get; init; }
}
