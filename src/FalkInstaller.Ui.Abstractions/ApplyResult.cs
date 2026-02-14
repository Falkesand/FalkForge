namespace FalkInstaller.Ui.Abstractions;

public readonly record struct ApplyResult(int ExitCode, string? ErrorMessage);
