namespace FalkForge.Studio.Shell;

public sealed class ValidationMessage
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public string? EditorKey { get; init; }
}
