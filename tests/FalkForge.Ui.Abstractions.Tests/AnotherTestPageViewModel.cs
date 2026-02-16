namespace FalkForge.Ui.Abstractions.Tests;

using FalkForge.Ui.Abstractions.ViewModels;

internal sealed class AnotherTestPageViewModel : InstallerPageViewModel
{
    public AnotherTestPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation) { }

    public override string Title => "Another Page";
    public override string Description => "Another test page";
}
