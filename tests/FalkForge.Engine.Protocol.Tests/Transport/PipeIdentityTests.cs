namespace FalkForge.Engine.Protocol.Tests.Transport;

using System.Runtime.Versioning;
using System.Security.Principal;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

/// <summary>
/// The elevation control pipe is created by the engine at medium integrity and opened by the
/// companion at high integrity. A Windows token exposes two different SIDs and only one of them
/// is stable across that elevation. Measured on a real machine on 2026-08-27, for the same
/// interactive user:
/// <code>
/// token        Owner SID                     User SID
/// elevated     S-1-5-32-544                  S-1-5-21-...-1001
/// unelevated   S-1-5-21-...-1001             S-1-5-21-...-1001
/// </code>
/// Comparing Owner refuses the companion every time. Comparing User accepts it and still refuses
/// any other account. These tests pin that choice with hand-built SIDs, so they hold on an
/// unelevated build agent where the running process cannot show the difference itself.
/// </summary>
[SupportedOSPlatform("windows")]
public class PipeIdentityTests
{
    // Shapes taken from the measurement above. The account RID is arbitrary; what matters is that
    // the account SID is a domain-style SID and the administrators SID is the well-known alias.
    private static readonly SecurityIdentifier Account =
        new("S-1-5-21-1111111111-2222222222-3333333333-1001");
    private static readonly SecurityIdentifier Administrators =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier OtherAccount =
        new("S-1-5-21-1111111111-2222222222-3333333333-1002");

    [Fact]
    public void Accepts_a_pipe_owned_by_the_same_account_as_the_connecting_token()
    {
        // The real elevated case: the engine created the pipe owned by the account SID, and the
        // companion's token User is that same account SID even though its Owner is not.
        Assert.True(PipeIdentity.IsAcceptableOwner(Account, Account));
    }

    [Fact]
    public void Refuses_a_pipe_owned_by_the_administrators_alias()
    {
        // Guards against someone "fixing" this by comparing against Owner again: a pipe owned by
        // BUILTIN\Administrators is reachable by every admin-token process on the machine, not by
        // this install's companion alone.
        Assert.False(PipeIdentity.IsAcceptableOwner(Administrators, Account));
    }

    [Fact]
    public void Refuses_a_pipe_owned_by_a_different_account()
    {
        Assert.False(PipeIdentity.IsAcceptableOwner(OtherAccount, Account));
    }

    [Fact]
    public void Refuses_when_the_pipe_owner_could_not_be_read()
    {
        // Fail closed. An unreadable descriptor is not evidence of a trustworthy peer.
        Assert.False(PipeIdentity.IsAcceptableOwner(null, Account));
    }

    [Fact]
    public void Refuses_when_the_connecting_token_has_no_user_sid()
    {
        Assert.False(PipeIdentity.IsAcceptableOwner(Account, null));
    }
}
