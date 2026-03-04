namespace FalkForge.Validation;

public sealed record ValidationMessage(ValidationSeverity Severity, string Code, string Message);