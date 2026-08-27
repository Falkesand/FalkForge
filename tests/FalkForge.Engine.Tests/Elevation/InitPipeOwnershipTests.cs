namespace FalkForge.Engine.Tests.Elevation;

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using FalkForge.Engine.Pipeline;
using Xunit;

/// <summary>
/// The init pipe delivers the 32-byte HMAC secret that authenticates the whole elevation channel.
/// Its descriptor must name the account SID, not the token's Owner SID: when the engine itself
/// runs elevated, Owner is BUILTIN\Administrators, so every admin-token process on the machine
/// could open the pipe and read the secret.
/// </summary>
[SupportedOSPlatform("windows")]
public class InitPipeOwnershipTests
{
    [Fact]
    public async Task Init_pipe_is_owned_by_the_account_and_grants_nobody_else()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        var pipeName = $"falkforge_init_test_{Guid.NewGuid():N}";
        var initPipeResult = NamedPipeElevationGateway.CreateInitPipe(pipeName);
        Assert.True(initPipeResult.IsSuccess);
        await using var initPipe = initPipeResult.Value;

        using var identity = WindowsIdentity.GetCurrent();
        var account = identity.User;
        Assert.NotNull(account);

        using var probe = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
        await probe.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);

        var security = probe.GetAccessControl();
        Assert.Equal(account, security.GetOwner(typeof(SecurityIdentifier)));

        var rules = security.GetAccessRules(
            includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        Assert.All(rules.Cast<PipeAccessRule>(), rule => Assert.Equal(account, rule.IdentityReference));
    }
}
