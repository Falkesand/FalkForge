namespace FalkForge.Validation;

public sealed class ValidationResult
{
    private readonly List<ValidationMessage> _messages = [];

    public bool IsValid => !_messages.Any(m => m.Severity == ValidationSeverity.Error);
    public IReadOnlyList<ValidationMessage> Messages => _messages;
    public IEnumerable<ValidationMessage> Errors => _messages.Where(m => m.Severity == ValidationSeverity.Error);
    public IEnumerable<ValidationMessage> Warnings => _messages.Where(m => m.Severity == ValidationSeverity.Warning);

    internal void AddError(string code, string message) =>
        _messages.Add(new ValidationMessage(ValidationSeverity.Error, code, message));

    internal void AddWarning(string code, string message) =>
        _messages.Add(new ValidationMessage(ValidationSeverity.Warning, code, message));
}
