namespace FalkForge.Extensions.Iis.Models;

public sealed class AppPoolModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ManagedRuntimeVersion { get; init; } = "v4.0";
    public ManagedPipelineMode ManagedPipelineMode { get; init; } = ManagedPipelineMode.Integrated;
    public bool Enable32BitAppOnWin64 { get; init; }
    public AppPoolIdentityType IdentityType { get; init; } = AppPoolIdentityType.ApplicationPoolIdentity;
    public string? UserName { get; init; }

    /// <summary>
    /// Name of an MSI property that supplies the <c>SpecificUser</c> identity password <b>at run time</b> —
    /// the secure, recommended path. The value is never stored in the MSI: the execution seam emits an
    /// immediate <c>SetProperty</c> (type 51) custom action that copies <c>[PasswordProperty]</c> into the
    /// deferred install action's <c>CustomActionData</c>, and the value is supplied at run time via
    /// <c>IInstallerEngine.SetSecureProperty</c>. Mutually exclusive with <see cref="Password"/>.
    /// </summary>
    public string? PasswordProperty { get; init; }

    /// <summary>
    /// Literal <c>SpecificUser</c> identity password. <b>Discouraged</b>: a literal here is embedded in
    /// plaintext in the compiled MSI (IIS012 warning), mirroring the SQL015/REG007/CTB011 posture. Prefer
    /// <see cref="PasswordProperty"/> with <c>SetSecureProperty</c>. Mutually exclusive with
    /// <see cref="PasswordProperty"/>.
    /// </summary>
    public string? Password { get; init; }
    public int MaxProcesses { get; init; } = 1;
    public int RecycleMinutes { get; init; } = 1740;
    public int IdleTimeoutMinutes { get; init; } = 20;
}