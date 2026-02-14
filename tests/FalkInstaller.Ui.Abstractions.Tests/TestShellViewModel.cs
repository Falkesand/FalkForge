namespace FalkInstaller.Ui.Abstractions.Tests;

using FalkInstaller.Ui.Abstractions.ViewModels;

internal sealed class TestShellViewModel : InstallerShellViewModel
{
    public int PageChangedCount { get; private set; }

    public TestShellViewModel(IInstallerEngine engine) : base(engine) { }

    public new void RegisterPage(InstallerPageViewModel page) => base.RegisterPage(page);

    protected override void OnCurrentPageChanged()
    {
        PageChangedCount++;
    }
}
