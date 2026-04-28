namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Knob bag controlling <see cref="MsiRecipeBuilder"/> behavior. All members
/// have safe defaults so callers can pass <c>new MsiRecipeBuildOptions()</c>
/// for the production path; tests, MSM/MSP/MST compilers, and dry-run tooling
/// override individual knobs as needed.
/// </summary>
public sealed record MsiRecipeBuildOptions
{
    /// <summary>How files are ordered for cabinet packing and File.Sequence assignment.</summary>
    public FileSequencingStrategy Sequencing { get; init; } = FileSequencingStrategy.FileIdOrdinal;

    /// <summary>
    /// When <c>true</c>, the builder hashes each registered stream payload at
    /// recipe-build time so reproducibility hashing has SHA-256 already
    /// available downstream.
    /// </summary>
    public bool EagerStreamHashing { get; init; } = true;

    /// <summary>
    /// Threshold in bytes above which a stream payload is kept on disk
    /// (<see cref="StreamSource.FilePath"/>) rather than buffered in memory.
    /// Default: 256 KiB.
    /// </summary>
    public int MaxInMemoryStreamBytes { get; init; } = 256 * 1024;
}
