namespace FalkForge.Models;

/// <summary>
/// Canonical values for <see cref="EnvironmentVariableModel.Part"/>, mirroring the WiX
/// <c>Environment/@Part</c> attribute. <c>Part</c> governs where the authored value sits
/// relative to any existing value in the MSI <c>Environment</c> table <c>Value</c> column —
/// the <c>[~]</c> token marks the position of the existing value:
/// <list type="bullet">
///   <item><see cref="All"/> — replace the whole value (no <c>[~]</c>).</item>
///   <item><see cref="First"/> — prepend: new text, separator, then <c>[~]</c>.</item>
///   <item><see cref="Last"/> — append: <c>[~]</c>, separator, then new text.</item>
/// </list>
/// When <see cref="EnvironmentVariableModel.Part"/> is <see langword="null"/> the model's
/// <see cref="EnvironmentVariableAction"/> drives placement instead (back-compatible default).
/// Values are compared case-insensitively by the compiler.
/// </summary>
public static class EnvironmentVariablePart
{
    /// <summary>Replace the entire value (no <c>[~]</c> existing-value token).</summary>
    public const string All = "all";

    /// <summary>Prepend the new text ahead of the existing value.</summary>
    public const string First = "first";

    /// <summary>Append the new text after the existing value.</summary>
    public const string Last = "last";
}
