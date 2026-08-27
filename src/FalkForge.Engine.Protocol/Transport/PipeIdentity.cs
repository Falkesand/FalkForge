namespace FalkForge.Engine.Protocol.Transport;

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Owns every SID decision the pipe transport makes.
/// <para>
/// A Windows access token carries two SIDs that are equal for an ordinary process and different
/// for an elevated one. <c>Owner</c> is the SID Windows stamps on objects the process creates,
/// and elevation changes it to BUILTIN\Administrators. <c>User</c> is the account the process
/// runs as, and elevation does not change it. Measured on a real machine on 2026-08-27 for one
/// interactive user: the elevated token reported Owner <c>S-1-5-32-544</c> and User
/// <c>S-1-5-21-...-1001</c>, the unelevated token reported <c>S-1-5-21-...-1001</c> for both.
/// </para>
/// <para>
/// The engine creates the elevation control pipe at medium integrity and the companion opens it
/// at high integrity, so any identity check built on <c>Owner</c> refuses the companion every
/// time. Everything here uses <c>User</c> instead, which makes the check hold in both directions
/// and in an all-elevated run, and which also stops an elevated engine from creating a pipe every
/// admin-token process on the machine can open.
/// </para>
/// </summary>
internal static class PipeIdentity
{
    /// <summary>
    /// The account SID of the running process, or <see langword="null"/> when the token does not
    /// expose one. Callers treat null as "cannot establish identity" and fail closed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static SecurityIdentifier? CurrentAccountSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User;
    }

    /// <summary>
    /// Builds a descriptor that grants full control to <paramref name="account"/> and nobody else,
    /// and names <paramref name="account"/> as the owner so a connecting client can recognise it.
    /// A DACL holding a single allow ACE denies every other principal by default.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static PipeSecurity CreateAccountOnlySecurity(SecurityIdentifier account)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(
            new PipeAccessRule(account, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.SetOwner(account);
        return security;
    }

    /// <summary>
    /// Decides whether a pipe with owner <paramref name="pipeOwner"/> may be talked to by a
    /// process whose account SID is <paramref name="connectingAccount"/>. Pure, so it can be
    /// tested against the measured SID pairs without a pipe and without elevation.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static bool IsAcceptableOwner(
        SecurityIdentifier? pipeOwner, SecurityIdentifier? connectingAccount)
        => pipeOwner is not null
            && connectingAccount is not null
            && pipeOwner.Equals(connectingAccount);
}
