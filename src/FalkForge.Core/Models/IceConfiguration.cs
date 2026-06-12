namespace FalkForge.Models;

public sealed record IceConfiguration
{
    public bool Enabled { get; init; } = true;
    public string? CubFilePath { get; init; }
    public IReadOnlyList<string> SuppressedIces { get; init; } = [];
    public bool WarningsAsErrors { get; init; }
    public string? ReportPath { get; init; }

    /// <summary>
    /// When true, silently skips ICE validation if darice.cub cannot be found on the machine
    /// (lenient / opt-out mode). Restores the old behavior for environments that genuinely
    /// lack the Windows SDK.
    /// <para>
    /// Default is <c>false</c> (strict): a missing darice.cub is a typed failure so callers
    /// know ICE was never run and can act accordingly.
    /// </para>
    /// </summary>
    public bool SkipWhenCubUnavailable { get; init; }
}
