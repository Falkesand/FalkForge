namespace FalkInstaller.Extensions.Firewall;

[Flags]
public enum FirewallProfile
{
    Domain = 1,
    Private = 2,
    Public = 4,
    All = Domain | Private | Public
}
