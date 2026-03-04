namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Represents a version range with optional inclusive/exclusive lower and upper bounds.
/// </summary>
public readonly record struct VersionRange(
    Version? MinVersion,
    Version? MaxVersion,
    bool MinInclusive,
    bool MaxInclusive)
{
    /// <summary>
    ///     Determines whether the specified version falls within this range.
    /// </summary>
    public bool IsSatisfiedBy(Version version)
    {
        if (MinVersion is not null)
        {
            var cmp = version.CompareTo(MinVersion);
            if (MinInclusive ? cmp < 0 : cmp <= 0)
                return false;
        }

        if (MaxVersion is not null)
        {
            var cmp = version.CompareTo(MaxVersion);
            if (MaxInclusive ? cmp > 0 : cmp >= 0)
                return false;
        }

        return true;
    }
}