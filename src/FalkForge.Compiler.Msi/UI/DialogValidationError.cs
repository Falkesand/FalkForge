namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// A compile-time dialog validation error produced by <see cref="DialogCustomizationValidator"/>.
/// </summary>
/// <param name="Code">
/// Rule identifier (e.g. "DLG001", "DLG002").
/// </param>
/// <param name="Message">
/// Human-readable description of the violation.
/// </param>
internal sealed record DialogValidationError(string Code, string Message);
