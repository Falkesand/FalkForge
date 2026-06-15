namespace FalkForge.Decompiler;

/// <summary>
/// Single source of truth for the MSI cabinet stream-name allowlist and the cumulative
/// decompression-bomb budget shared by every caller that extracts embedded cabinets.
///
/// <para>
/// The cabinet stream name is read from the attacker-controlled MSI <c>Media.Cabinet</c>
/// column and interpolated into an MSI-SQL <c>WHERE</c> clause by both
/// <see cref="MsiPayloadExtractor"/> (migration) and <c>FalkForge.Cli.MsiExtractor</c>
/// (the <c>forge extract</c> command). Centralising the allowlist here keeps the two call
/// sites byte-identical — a second hand-rolled copy could drift and reopen the injection
/// hole (A03: Injection).
/// </para>
/// </summary>
public static class MsiStreamName
{
    /// <summary>
    /// Decompression-bomb guard: the cumulative uncompressed cabinet bytes a single MSI may
    /// expand to. 4 GiB is generous for legitimate installers yet bounds the memory a hostile
    /// (zip-bomb) MSI can force a process to allocate. Callers pass this to
    /// <c>CabinetExtractor.Extract(stream, maxTotalBytes)</c> and decrement it across cabs so a
    /// multi-cab bomb cannot bypass the cap by spreading bytes over several cabinets.
    /// </summary>
    public const long MaxTotalUncompressedCabinetBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Validates an MSI cabinet stream name against a strict allowlist before it is
    /// interpolated into an MSI-SQL <c>WHERE</c> clause. MSI stream names are at most
    /// 62 characters; here we additionally restrict to letters, digits, dot, underscore,
    /// and hyphen so no MSI-SQL metacharacter (notably a single quote) can be injected.
    /// </summary>
    public static bool IsValid(string streamName)
    {
        if (string.IsNullOrEmpty(streamName) || streamName.Length > 62)
            return false;

        foreach (var ch in streamName)
        {
            var ok = ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '.' or '_' or '-';
            if (!ok)
                return false;
        }

        return true;
    }
}
