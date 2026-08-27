namespace FalkForge.Engine.Protocol.Tests.Transport;

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

/// <summary>
/// The elevation control pipe is created by an unelevated engine and opened by an elevated
/// companion. These tests cover what can be proved without elevation: the descriptor the server
/// writes, and that a client can read that descriptor back over an ordinary connection.
/// </summary>
[SupportedOSPlatform("windows")]
public class PipeOwnerVerificationTests
{
    private static PipeConnectionOptions NewOptions(string pipeName) => new()
    {
        PipeName = pipeName,
        SharedSecret = RandomNumberGenerator.GetBytes(32),
        ConnectionTimeout = TimeSpan.FromSeconds(5)
    };

    [Fact]
    public async Task Server_owns_its_pipe_with_the_account_sid_and_grants_nobody_else()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        var pipeName = $"test-{Guid.NewGuid()}";
        await using var server = new PipeServer(NewOptions(pipeName), _ => Task.CompletedTask);
        Assert.True(server.CreateListener().IsSuccess);

        using var identity = WindowsIdentity.GetCurrent();
        var account = identity.User;
        Assert.NotNull(account);

        using var probe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await probe.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);

        var security = probe.GetAccessControl();
        Assert.Equal(account, security.GetOwner(typeof(SecurityIdentifier)));

        var rules = security.GetAccessRules(
            includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        Assert.All(rules.Cast<PipeAccessRule>(), rule => Assert.Equal(account, rule.IdentityReference));
    }

    [Fact]
    public async Task A_client_can_read_the_descriptor_the_server_wrote()
    {
        // Settles the load-bearing question for the whole fix: the client-side owner check needs
        // READ_CONTROL on the pipe handle, and the server's DACL is what grants it. This test
        // holds the DACL and the connecting token to the same account SID the elevated companion
        // will present, so a pass here says the companion gets the same granted access.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        var pipeName = $"test-{Guid.NewGuid()}";
        await using var server = new PipeServer(NewOptions(pipeName), _ => Task.CompletedTask);
        Assert.True(server.CreateListener().IsSuccess);

        using var probe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await probe.ConnectAsync((int)TimeSpan.FromSeconds(5).TotalMilliseconds);

        var owner = probe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

        Assert.NotNull(owner);
        Assert.True(PipeIdentity.IsAcceptableOwner(owner, PipeIdentity.CurrentAccountSid()));
    }
}
