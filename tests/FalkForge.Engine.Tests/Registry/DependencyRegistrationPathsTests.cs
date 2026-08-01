namespace FalkForge.Engine.Tests.Registry;

using FalkForge.Platform.Dependencies;
using Xunit;

/// <summary>
/// Pins the registry path layout used by the runtime dependency-enforcement write/read side
/// (<see cref="DependencyRegistrar"/>, <see cref="FalkForge.Engine.Detection.DependencyDetector"/>) to be
/// byte-identical to the MSI-authored layout in
/// <c>FalkForge.Extensions.Dependency.DependencyTableContributor</c> (lines 45 and 77 there) — the
/// WiX/Burn-compatible convention both must agree on so a bundle-enforced dependency and an MSI-authored
/// one are visible to each other.
/// </summary>
public sealed class DependencyRegistrationPathsTests
{
    [Fact]
    public void ProviderKeyPath_MatchesMsiTableContributorLayout()
    {
        Assert.Equal(
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp",
            DependencyRegistrationPaths.ProviderKeyPath("MyApp"));
    }

    [Fact]
    public void DependentsKeyPath_MatchesMsiTableContributorLayout()
    {
        Assert.Equal(
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents",
            DependencyRegistrationPaths.DependentsKeyPath("MyApp"));
    }

    [Fact]
    public void ConsumerKeyPath_MatchesMsiTableContributorLayout()
    {
        Assert.Equal(
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents\OtherApp",
            DependencyRegistrationPaths.ConsumerKeyPath("MyApp", "OtherApp"));
    }

    [Theory]
    [InlineData(InstallScope.PerMachine, RegistryRoot.LocalMachine)]
    [InlineData(InstallScope.PerUser, RegistryRoot.CurrentUser)]
    public void WriteRootForScope_MirrorsScope(InstallScope scope, RegistryRoot expectedRoot)
    {
        Assert.Equal(expectedRoot, DependencyRegistrationPaths.WriteRootForScope(scope));
    }

    [Fact]
    public void ReadRoots_ContainsBothLocalMachineAndCurrentUser()
    {
        // A per-user consumer of a per-machine provider (or vice versa) is a real shape —
        // detection must union both roots, not just the writer's own scope.
        Assert.Contains(RegistryRoot.LocalMachine, DependencyRegistrationPaths.ReadRoots);
        Assert.Contains(RegistryRoot.CurrentUser, DependencyRegistrationPaths.ReadRoots);
        Assert.Equal(2, DependencyRegistrationPaths.ReadRoots.Count);
    }
}
