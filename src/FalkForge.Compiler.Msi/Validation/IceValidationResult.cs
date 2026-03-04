namespace FalkForge.Compiler.Msi.Validation;

public sealed class IceValidationResult
{
    private IceValidationResult(IReadOnlyList<IceMessage> messages)
    {
        Messages = messages;
        Errors = messages.Where(m => m.Severity == IceMessageSeverity.Error).ToList();
        Warnings = messages.Where(m => m.Severity == IceMessageSeverity.Warning).ToList();
        Failures = messages.Where(m => m.Severity == IceMessageSeverity.Failure).ToList();
    }

    public IReadOnlyList<IceMessage> Messages { get; }
    public IReadOnlyList<IceMessage> Errors { get; }
    public IReadOnlyList<IceMessage> Warnings { get; }
    public IReadOnlyList<IceMessage> Failures { get; }
    public bool IsValid => Errors.Count == 0 && Failures.Count == 0;

    public static IceValidationResult Success()
    {
        return new IceValidationResult([]);
    }

    public static IceValidationResult FromMessages(IReadOnlyList<IceMessage> messages)
    {
        return new IceValidationResult(messages);
    }
}