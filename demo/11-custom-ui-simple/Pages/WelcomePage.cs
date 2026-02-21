namespace CustomUiSimple.Pages;

using CustomUiSimple.Views;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;

public class WelcomePage : InstallerPage<WelcomeView>
{
    public override string Title => Localize("Welcome.Title");

    public string ProductName => Localize("Welcome.ProductName");
    public string Description => Localize("Welcome.Description");

    public override PageResult OnNext() => PageResult.Next;
    public override bool CanGoBack => false;
}
