namespace FalkForge.Diagnostics;

/// <summary>
/// Severity level for a structured log entry, shared by the logging contract
/// (<see cref="IFalkLogger"/>) and the Engine/UI wire protocol (<c>LogMessage</c>).
/// </summary>
/// <remarks>
/// The numeric values are part of the wire format (see <c>LogCodec</c> in
/// <c>FalkForge.Engine.Protocol</c>, which writes the underlying <see langword="int"/>
/// directly) — do not renumber existing members.
/// </remarks>
public enum LogLevel
{
    Verbose = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}
