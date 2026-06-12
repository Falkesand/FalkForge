namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Verifies that <see cref="CustomShellViewModel.IsDryRun"/> correctly reflects
/// the engine manifest's IsDryRun field. The XAML banner binds to this property
/// to show "DRY RUN — no changes will be made" when true.
/// </summary>
[Collection("WPF")]
public sealed class CustomShellViewModelDryRunTests
{
    private static CustomShellViewModel MakeViewModel(bool isDryRun)
    {
        var engine = new TestInstallerEngine();
        engine.Manifest = new InstallerManifest
        {
            Name = "TestProduct",
            Manufacturer = "TestCorp",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Packages = [],
            Scope = InstallScope.PerMachine,
            IsDryRun = isDryRun
        };

        return new CustomShellViewModel([], engine, new InstallerState());
    }

    [WpfFact]
    public void IsDryRun_False_WhenManifestIsDryRunFalse()
    {
        var vm = MakeViewModel(isDryRun: false);
        Assert.False(vm.IsDryRun, "IsDryRun must be false when manifest IsDryRun is false");
    }

    [WpfFact]
    public void IsDryRun_True_WhenManifestIsDryRunTrue()
    {
        var vm = MakeViewModel(isDryRun: true);
        Assert.True(vm.IsDryRun, "IsDryRun must be true when manifest IsDryRun is true");
    }
}
