namespace FalkForge.Compiler.Msi.Validation;

public sealed class IceValidationResult
{
    public IReadOnlyList<IceMessage> Messages { get; }
    public IReadOnlyList<IceMessage> Errors { get; }
    public IReadOnlyList<IceMessage> Warnings { get; }
    public IReadOnlyList<IceMessage> Failures { get; }
    public bool IsValid => Errors.Count == 0 && Failures.Count == 0;

    private IceValidationResult(IReadOnlyList<IceMessage> messages)
    {
        Messages = messages;
        Errors = messages.Where(m => m.Severity == IceMessageSeverity.Error).ToList();
        Warnings = messages.Where(m => m.Severity == IceMessageSeverity.Warning).ToList();
        Failures = messages.Where(m => m.Severity == IceMessageSeverity.Failure).ToList();
    }

    public static IceValidationResult Success() => new([]);
    public static IceValidationResult FromMessages(IReadOnlyList<IceMessage> messages) => new(messages);
}
