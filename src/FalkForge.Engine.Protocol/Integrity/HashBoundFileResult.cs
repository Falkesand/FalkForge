namespace FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// What <see cref="HashBoundFile.Open(string, string)"/> returns: a status, and on success the
/// still-open handle plus the path that handle was opened from.
/// </summary>
/// <param name="Status">Why the call succeeded or failed.</param>
/// <param name="Stream">
/// The open, still-held file handle when <paramref name="Status"/> is
/// <see cref="HashBoundFileStatus.Verified"/>, and <see langword="null"/> otherwise. The caller
/// owns disposal. On every failure status the helper has already disposed whatever it opened.
/// </param>
/// <param name="ResolvedPath">
/// The path the returned handle was opened from, when <paramref name="Status"/> is
/// <see cref="HashBoundFileStatus.Verified"/>. <see langword="null"/> otherwise.
/// </param>
/// <param name="Detail">
/// Extra context for the failure, meant to be embedded in the caller's own message:
/// the computed digest as upper-case hex for <see cref="HashBoundFileStatus.HashMismatch"/>,
/// the operating system's error text for <see cref="HashBoundFileStatus.FileNotFound"/>,
/// <see cref="HashBoundFileStatus.OpenFailed"/> and <see cref="HashBoundFileStatus.ReadFailed"/>,
/// and <see langword="null"/> for every other status.
/// </param>
public readonly record struct HashBoundFileResult(
    HashBoundFileStatus Status,
    FileStream? Stream,
    string? ResolvedPath,
    string? Detail);
