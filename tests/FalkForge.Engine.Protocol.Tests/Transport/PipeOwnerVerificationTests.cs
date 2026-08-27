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

    [Fact]
    public async Task Client_refuses_a_pipe_whose_owner_is_not_this_account()
    {
        // The load-bearing test for this branch. It must fail if anyone reverts the identity fix,
        // on a developer box and on CI alike, so it does not depend on the token being able to
        // create a foreign-owned pipe. Measured 2026-08-27 on an ordinary unelevated Windows
        // token: all 12 group SIDs were refused as pipe owners with ERROR_INVALID_OWNER, so a
        // test that needs a real foreign-owned pipe silently skips there.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var pipeName = $"test-{Guid.NewGuid()}";
        var serverOptions = NewOptions(pipeName);
        await using var server = new PipeServer(serverOptions, _ => Task.CompletedTask);
        Assert.True(server.CreateListener().IsSuccess);

        var securityEvents = new List<string>();
        var commandDispatched = false;

        // A well-known SID no ordinary account equals. The seam replaces the descriptor read, so
        // the pipe on the wire is the real, account-owned one and only the identity input changes.
        var foreignSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;

        await using var client = new PipeClient(
            new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = serverOptions.SharedSecret,
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                OnSecurityEvent = securityEvents.Add
            },
            _ =>
            {
                commandDispatched = true;
                return Task.CompletedTask;
            })
        {
            PipeOwnerSidOverride = () => foreignSid
        };

        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, result.Error.Kind);
        Assert.False(client.IsConnected);
        Assert.False(commandDispatched);
        Assert.Contains(securityEvents, e => e.Contains("owner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Client_refuses_a_pipe_whose_owner_could_not_be_read()
    {
        // Fail closed. An unreadable descriptor must refuse, not pass.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var pipeName = $"test-{Guid.NewGuid()}";
        var serverOptions = NewOptions(pipeName);
        await using var server = new PipeServer(serverOptions, _ => Task.CompletedTask);
        Assert.True(server.CreateListener().IsSuccess);

        await using var client = new PipeClient(
            new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = serverOptions.SharedSecret,
                ConnectionTimeout = TimeSpan.FromSeconds(5)
            },
            _ => Task.CompletedTask)
        {
            PipeOwnerSidOverride = () => null
        };

        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.False(client.IsConnected);
    }

    /// <summary>
    /// Finds a SID the current token holds that is not the account SID, so a test can create a
    /// pipe the connecting process must refuse. Setting an owner only succeeds for a SID the
    /// token actually carries, so this enumerates the token's groups rather than guessing a
    /// well-known SID that a given machine or build agent may not have.
    /// <para>
    /// Measured 2026-08-27 on an ordinary unelevated Windows token: 12 group SIDs, 12 refusals
    /// with ERROR_INVALID_OWNER, zero settable. This returns null on such a machine and the test
    /// below skips. That is why it is a bonus test and not the branch's real RED.
    /// </para>
    /// </summary>
    private static SecurityIdentifier? FindForeignOwnableSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var account = identity.User;
        if (account is null || identity.Groups is null)
            return null;

        foreach (var group in identity.Groups)
        {
            if (group is not SecurityIdentifier sid || sid.Equals(account))
                continue;

            // Prove it is actually settable as an owner before handing it to a test, by creating
            // and discarding a throwaway pipe with that owner.
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(
                    new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
                security.SetOwner(sid);

                using var probe = NamedPipeServerStreamAcl.Create(
                    $"test-owner-probe-{Guid.NewGuid()}",
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    security);
                return sid;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                or System.Security.SecurityException or PrivilegeNotHeldException)
            {
                // Not settable as an owner by this token; try the next group.
            }
        }

        return null;
    }

    [Fact]
    public async Task Client_refuses_a_real_pipe_owned_by_another_sid()
    {
        // Bonus coverage over a genuinely foreign-owned pipe, for the machines that can build one.
        // The defect this branch fixes had this shape: the client rejected a peer by throwing out
        // of ConnectAsync instead of returning a Result, and the elevated companion died with no
        // diagnostic. The refusal itself is correct and must stay. What must change is that it
        // arrives as a logged Result the caller can act on.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var foreign = FindForeignOwnableSid();
        if (foreign is null)
            Assert.Skip("This token carries no group SID it can set as an owner, so a " +
                        "foreign-owned pipe cannot be created here.");

        using var identity = WindowsIdentity.GetCurrent();
        var account = identity.User;
        Assert.NotNull(account);

        // Owner is the foreign SID; the DACL still admits this account so the connection itself
        // succeeds and the test exercises the identity check rather than an access denial.
        var security = new PipeSecurity();
        security.AddAccessRule(
            new PipeAccessRule(account, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(
            new PipeAccessRule(foreign, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.SetOwner(foreign);

        var pipeName = $"test-{Guid.NewGuid()}";
        await using var rogue = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);

        var securityEvents = new List<string>();
        var options = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            OnSecurityEvent = securityEvents.Add
        };

        var commandDispatched = false;
        await using var client = new PipeClient(options, _ =>
        {
            commandDispatched = true;
            return Task.CompletedTask;
        });

        // The rogue pipe has no server logic behind it, so an implementation that let the client
        // walk past the owner check would block in the handshake read. The token bounds that.
        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, result.Error.Kind);
        Assert.False(client.IsConnected);
        Assert.False(commandDispatched);
        Assert.Contains(securityEvents, e => e.Contains("owner", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Client_treats_a_disconnected_pipe_during_owner_read_as_unavailable()
    {
        // Measured 2026-08-27: GetAccessControl() on a pipe that disconnected between ConnectAsync
        // succeeding and this read throws System.InvalidOperationException("The pipe has been
        // disconnected."), not one of the four types the old catch filter listed. That is an
        // ordinary race, not evidence of an untrustworthy peer, so it must come back as
        // TransportError (unavailable), not HandshakeError (refused) — and it must come back as a
        // Result at all, not escape ConnectAsync, which PipeOwnerSidOverride throwing simulates
        // without needing a real race.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var pipeName = $"test-{Guid.NewGuid()}";
        var serverOptions = NewOptions(pipeName);
        await using var server = new PipeServer(serverOptions, _ => Task.CompletedTask);
        Assert.True(server.CreateListener().IsSuccess);

        var securityEvents = new List<string>();
        await using var client = new PipeClient(
            new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = serverOptions.SharedSecret,
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                OnSecurityEvent = securityEvents.Add
            },
            _ => Task.CompletedTask)
        {
            PipeOwnerSidOverride = () => throw new InvalidOperationException(
                "The pipe has been disconnected.")
        };

        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.TransportError, result.Error.Kind);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Client_fails_closed_when_owner_read_throws_an_unexpected_exception()
    {
        // NotSupportedException is one of the two additional types GetAccessControl() can throw
        // (invalid handle or no security on the object) that the old catch filter did not list.
        // Unlike a disconnect, this is not an ordinary race — fail closed as a refusal
        // (HandshakeError), matching every other "could not read the descriptor" path here.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var pipeName = $"test-{Guid.NewGuid()}";
        var serverOptions = NewOptions(pipeName);
        await using var server = new PipeServer(serverOptions, _ => Task.CompletedTask);
        Assert.True(server.CreateListener().IsSuccess);

        var securityEvents = new List<string>();
        await using var client = new PipeClient(
            new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = serverOptions.SharedSecret,
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                OnSecurityEvent = securityEvents.Add
            },
            _ => Task.CompletedTask)
        {
            PipeOwnerSidOverride = () => throw new NotSupportedException("No security on the object.")
        };

        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, result.Error.Kind);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Client_accepts_a_pipe_owned_by_its_own_account()
    {
        // Control for the tests above. Without it, a client that refused every pipe would look
        // like a pass.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Pipe security descriptors are a Windows concept.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var options = NewOptions($"test-{Guid.NewGuid()}");
        await using var server = new PipeServer(options, _ => Task.CompletedTask);
        var serverTask = server.StartAsync(cts.Token);

        await using var client = new PipeClient(options, _ => Task.CompletedTask);
        var result = await client.ConnectAsync(cts.Token);
        var serverResult = await serverTask;

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(serverResult.IsSuccess);
        Assert.True(client.IsConnected);
    }
}
