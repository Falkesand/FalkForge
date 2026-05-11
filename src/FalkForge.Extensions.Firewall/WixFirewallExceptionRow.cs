namespace FalkForge.Extensions.Firewall;

/// <summary>
/// Raw row read from the MSI <c>WixFirewallException</c> table by the
/// decompile pipeline. Column order mirrors the write-side
/// <see cref="FirewallTableContributor.GetRows"/> output.
/// </summary>
public sealed record WixFirewallExceptionRow(
    string  Name,
    string? RemoteAddresses,
    string? Port,
    int     Protocol,
    string? Program,
    int     Profile,
    int     Direction,
    int     Action,
    string? Component_,
    string? Description,
    string? Condition);
