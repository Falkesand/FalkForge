namespace FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// Outcome of <see cref="HashBoundFile.Open(string, string)"/>. Each caller maps these to its own
/// error type and wording, so the shared helper never has to guess whether a failure is a security
/// failure or an execution failure for the caller in question.
/// </summary>
public enum HashBoundFileStatus
{
    /// <summary>
    /// The file was opened, its bytes hashed, and the digest matched the expected value. This is
    /// the only status for which a stream is returned, and the caller owns disposing it.
    /// </summary>
    Verified = 0,

    /// <summary>
    /// The expected hash was not 64 hexadecimal characters. Nothing was opened. A malformed hash
    /// fails closed, exactly like a mismatch — it never means "skip the check".
    /// </summary>
    MalformedExpectedHash = 1,

    /// <summary>The file, or a directory on the way to it, does not exist.</summary>
    FileNotFound = 2,

    /// <summary>
    /// The file exists but could not be opened for shared read — another handle denies read
    /// sharing, or the caller lacks access.
    /// </summary>
    OpenFailed = 3,

    /// <summary>The file opened but reading its bytes failed part-way through.</summary>
    ReadFailed = 4,

    /// <summary>The file's bytes hash to something other than the expected digest.</summary>
    HashMismatch = 5,
}
