namespace FalkInstaller.Extensions.Iis.Models;

public sealed class AppPoolModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ManagedRuntimeVersion { get; init; } = "v4.0";
    public ManagedPipelineMode ManagedPipelineMode { get; init; } = ManagedPipelineMode.Integrated;
    public bool Enable32BitAppOnWin64 { get; init; }
    public AppPoolIdentityType IdentityType { get; init; } = AppPoolIdentityType.ApplicationPoolIdentity;
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public int MaxProcesses { get; init; } = 1;
    public int RecycleMinutes { get; init; } = 1740;
    public int IdleTimeoutMinutes { get; init; } = 20;
}
