namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Outcome of a successful <see cref="ElevationCompanionLocator"/> resolution.
/// <see cref="ResolvedPath"/> is null when the bundle legitimately carries no elevation companion
/// (explicit opt-out or design-time placeholder build) — a distinct state from a resolution
/// failure, which <see cref="Result{T}"/> cannot express with a null success value.
/// </summary>
/// <param name="ResolvedPath">Full path to the companion executable to embed, or null for none.</param>
internal readonly record struct ElevationCompanionResolution(string? ResolvedPath)
{
    /// <summary>The bundle carries no companion (opt-out / placeholder).</summary>
    internal static ElevationCompanionResolution None => new((string?)null);
}
