namespace FalkForge.Engine.Download;

internal sealed record UpdateCheckResult(UpdateInfo? Update)
{
    public static UpdateCheckResult None { get; } = new((UpdateInfo?)null);
}
