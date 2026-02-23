namespace FalkForge.Models;

public sealed record ReproducibleBuildOptions
{
    public long SourceDateEpoch { get; init; }

    public DateTime Timestamp => DateTimeOffset.FromUnixTimeSeconds(SourceDateEpoch).UtcDateTime;
}
